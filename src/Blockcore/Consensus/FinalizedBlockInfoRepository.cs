﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Blockcore.AsyncWork;
using Blockcore.Networks;
using Blockcore.Utilities;
using Blockcore.Utilities.Store;
using Microsoft.Extensions.Logging;
using NBitcoin;

namespace Blockcore.Consensus
{
    /// <summary>Provider of the last finalized block's height and hash.</summary>
    /// <remarks>
    /// Finalized block height is the height of the last block that can't be reorged.
    /// Blocks with height greater than finalized height can be reorged.
    /// <para>Finalized block height value is always <c>0</c> for blockchains without max reorg property.</para>
    ///
    /// The protection finality is to not allow rewinding the node bellow a certain height, whileh MaxReorgProtection does the same
    /// MaxReorg does not protect from chain with different work. MaxReorg determins the chain by its height
    /// while rewind uses work as indicator (the chain with most work is the winner),
    /// this can cause the scenario where a chain with lower height but more work triggeres a reorg and consensus tip ends up in a lower height.
    /// Now a second rewind can heppen that will go below MaxReorg of the initial hiegher (but lower work) tip,
    /// for that we need finality, even if such a reorg happens finality will prevent the second reorg that will vioulate MAxReorgProtection.
    ///
    /// A - - - - - - - - - - - - - - - - - [current tip]
    /// B   |    L - - - - -  - - [more work then chain A]
    /// C   L - - - - - [more work then chain B but violate max reorg]
    /// </remarks>
    public interface IFinalizedBlockInfoRepository : IDisposable
    {
        /// <summary>Gets the finalized block hash and height.</summary>
        /// <returns>Hash and height of a block that can't be reorged away from.</returns>
        HashHeightPair GetFinalizedBlockInfo();

        /// <summary>Loads the finalised block hash and height from the database.</summary>
        Task LoadFinalizedBlockInfoAsync(Network network);

        /// <summary>Saves the finalized block hash and height to the database if height is greater than the previous value.</summary>
        /// <param name="hash">Block hash.</param>
        /// <param name="height">Block height.</param>
        /// <returns><c>true</c> if new value was set, <c>false</c> if <paramref name="height"/> is lower or equal than current value.</returns>
        bool SaveFinalizedBlockHashAndHeight(uint256 hash, int height);
    }

    public class FinalizedBlockInfoRepository : IFinalizedBlockInfoRepository
    {
        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        private readonly IKeyValueRepository keyValueRepo;

        /// <summary>Database key under which the block height of the last finalized block height is stored.</summary>
        private const string FinalizedBlockKey = "finalizedBlock";

        /// <summary>Height and hash of a block that can't be reorged away from.</summary>
        private HashHeightPair finalizedBlockInfo;

        /// <summary>Queue of finalized infos to save.</summary>
        /// <remarks>All access should be protected by <see cref="queueLock"/>.</remarks>
        private readonly Queue<HashHeightPair> finalizedBlockInfosToSave;

        /// <summary>Protects access to <see cref="finalizedBlockInfosToSave"/>.</summary>
        private readonly object queueLock;

        /// <summary>Task that continously persists finalized block info to the database.</summary>
        private readonly Task finalizedBlockInfoPersistingTask;

        private readonly CancellationTokenSource cancellation;

        private readonly AsyncManualResetEvent queueUpdatedEvent;

        public FinalizedBlockInfoRepository(HashHeightPair finalizedBlockInfo)
        {
            this.finalizedBlockInfo = finalizedBlockInfo;
        }

        public FinalizedBlockInfoRepository(IKeyValueRepository keyValueRepo, ILoggerFactory loggerFactory, IAsyncProvider asyncProvider)
        {
            Guard.NotNull(keyValueRepo, nameof(keyValueRepo));
            Guard.NotNull(loggerFactory, nameof(loggerFactory));

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);

            this.keyValueRepo = keyValueRepo;
            this.finalizedBlockInfosToSave = new Queue<HashHeightPair>();
            this.queueLock = new object();

            this.queueUpdatedEvent = new AsyncManualResetEvent(false);
            this.cancellation = new CancellationTokenSource();
            this.finalizedBlockInfoPersistingTask = this.PersistFinalizedBlockInfoContinuouslyAsync();

            asyncProvider.RegisterTask($"{nameof(FinalizedBlockInfoRepository)}.{nameof(this.finalizedBlockInfoPersistingTask)}", this.finalizedBlockInfoPersistingTask);
        }

        private async Task PersistFinalizedBlockInfoContinuouslyAsync()
        {
            while (!this.cancellation.IsCancellationRequested)
            {
                try
                {
                    await this.queueUpdatedEvent.WaitAsync(this.cancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException excp)
                {
                    this.logger.LogError("Error was thrown: {message}", excp.Message);
                }

                this.queueUpdatedEvent.Reset();

                HashHeightPair lastFinalizedBlock = null;

                lock (this.queueLock)
                {
                    // In case there are more than 1 items we take only the very last one.
                    // There is no need to save each of them because they are sequential and
                    // each new item has greater height than previous one.
                    while (this.finalizedBlockInfosToSave.Count != 0)
                        lastFinalizedBlock = this.finalizedBlockInfosToSave.Dequeue();
                }

                if (lastFinalizedBlock == null)
                    continue;

                this.keyValueRepo.SaveValue(FinalizedBlockKey, lastFinalizedBlock);

                this.logger.LogDebug("Finalized info saved: '{0}'.", lastFinalizedBlock);
            }
        }

        /// <inheritdoc />
        public HashHeightPair GetFinalizedBlockInfo()
        {
            return this.finalizedBlockInfo;
        }

        /// <inheritdoc />
        public Task LoadFinalizedBlockInfoAsync(Network network)
        {
            Task task = Task.Run(() =>
            {
                var finalizedInfo = this.keyValueRepo.LoadValue<HashHeightPair>(FinalizedBlockKey);

                if (finalizedInfo == null)
                    finalizedInfo = new HashHeightPair(network.GenesisHash, 0);

                this.finalizedBlockInfo = finalizedInfo;
            });
            return task;
        }

        /// <inheritdoc />
        public bool SaveFinalizedBlockHashAndHeight(uint256 hash, int height)
        {
            if (this.finalizedBlockInfo != null && height <= this.finalizedBlockInfo.Height)
            {
                this.logger.LogTrace("(-)[CANT_GO_BACK]:false");
                return false;
            }

            // Creating a new variable instead of assigning new value right away
            // to this.finalizedBlockInfo is needed because before we enqueue it
            // this.finalizedBlockInfo might change due to race condition.
            var finalizedInfo = new HashHeightPair(hash, height);

            this.finalizedBlockInfo = finalizedInfo;

            lock (this.queueLock)
            {
                this.finalizedBlockInfosToSave.Enqueue(finalizedInfo);
                this.queueUpdatedEvent.Set();
            }

            return true;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.cancellation.Cancel();
            this.finalizedBlockInfoPersistingTask.GetAwaiter().GetResult();
        }
    }
}