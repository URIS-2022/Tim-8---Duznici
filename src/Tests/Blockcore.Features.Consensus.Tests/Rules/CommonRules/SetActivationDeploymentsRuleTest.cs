﻿using System;
using System.Threading.Tasks;
using Blockcore.Base.Deployments;
using Blockcore.Consensus.BlockInfo;
using Blockcore.Consensus.ScriptInfo;
using Blockcore.Features.Consensus.Rules.CommonRules;
using NBitcoin;
using Xunit;
using static Blockcore.Consensus.TransactionInfo.Transaction;

namespace Blockcore.Features.Consensus.Tests.Rules.CommonRules
{
    public class SetActivationDeploymentsRuleTest : TestConsensusRulesUnitTestBase
    {
        public SetActivationDeploymentsRuleTest()
        {
            this.ChainIndexer = GenerateChainWithHeight(5, this.network);
            this.consensusRules = InitializeConsensusRules();
        }

        [Fact]
        public async Task RunAsync_ValidBlock_SetsConsensusFlagsAsync()
        {
            this.nodeDeployments = new NodeDeployments(this.network, this.ChainIndexer);
            this.consensusRules = InitializeConsensusRules();

            Block block = this.network.CreateBlock();
            block.AddTransaction(this.network.CreateTransaction());
            block.UpdateMerkleRoot();
            block.Header.BlockTime = new DateTimeOffset(new DateTime(2017, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(5));
            block.Header.HashPrevBlock = this.ChainIndexer.Tip.HashBlock;
            block.Header.Nonce = RandomUtils.GetUInt32();

            this.ruleContext.ValidationContext.BlockToValidate = block;
            this.ruleContext.ValidationContext.ChainedHeaderToValidate = this.ChainIndexer.Tip;

            await this.consensusRules.RegisterRule<SetActivationDeploymentsPartialValidationRule>().RunAsync(this.ruleContext);

            Assert.NotNull(this.ruleContext.Flags);
            Assert.True(this.ruleContext.Flags.EnforceBIP30);
            Assert.False(this.ruleContext.Flags.EnforceBIP34);
            Assert.Equal(LockTimeFlags.None, this.ruleContext.Flags.LockTimeFlags);
            Assert.Equal(ScriptVerify.Mandatory, this.ruleContext.Flags.ScriptFlags);
        }
    }
}
