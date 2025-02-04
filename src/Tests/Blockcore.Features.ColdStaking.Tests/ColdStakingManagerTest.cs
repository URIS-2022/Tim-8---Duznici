﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using Blockcore.AsyncWork;
using Blockcore.Configuration;
using Blockcore.Consensus;
using Blockcore.Consensus.BlockInfo;
using Blockcore.Consensus.Chain;
using Blockcore.Consensus.ScriptInfo;
using Blockcore.Consensus.TransactionInfo;
using Blockcore.Features.Wallet;
using Blockcore.Features.Wallet.Database;
using Blockcore.Features.Wallet.Exceptions;
using Blockcore.Features.Wallet.Interfaces;
using Blockcore.Features.Wallet.Types;
using Blockcore.Interfaces;
using Blockcore.Signals;
using Blockcore.Tests.Common.Logging;
using Blockcore.Tests.Wallet.Common;
using Blockcore.Utilities;
using Moq;
using NBitcoin;
using Xunit;

namespace Blockcore.Features.ColdStaking.Tests
{
    public class ColdStakingManagerTest : LogsTestBase, IClassFixture<WalletFixture>
    {
        private readonly IBlockStore blockStore;
        private readonly WalletFixture walletFixture;

        public ColdStakingManagerTest(WalletFixture walletFixture)
        {
            this.blockStore = new Mock<IBlockStore>().Object;
            this.walletFixture = walletFixture;
            this.Network.StandardScriptsRegistry.RegisterStandardScriptTemplate(ColdStakingScriptTemplate.Instance);
        }

        public Transaction CreateColdStakingSetupTransaction(Wallet.Types.Wallet wallet, string password, HdAddress spendingAddress, PubKey destinationColdPubKey, PubKey destinationHotPubKey, HdAddress changeAddress, Money amount, Money fee)
        {
            TransactionOutputData spendingTransaction = wallet.walletStore.GetForAddress(spendingAddress.Address).ElementAt(0);
            var coin = new Coin(spendingTransaction.Id, (uint)spendingTransaction.Index, spendingTransaction.Amount, spendingTransaction.ScriptPubKey);

            Key privateKey = Key.Parse(wallet.EncryptedSeed, password, wallet.Network);

            Script script = ColdStakingScriptTemplate.Instance.GenerateScriptPubKey(destinationHotPubKey.Hash, destinationColdPubKey.Hash);

            var builder = new TransactionBuilder(wallet.Network);
            builder.Extensions.Add(new ColdStakingBuilderExtension(false));
            Transaction tx = builder
                .AddCoins(new List<Coin> { coin })
                .AddKeys(new ExtKey(privateKey, wallet.ChainCode).Derive(new KeyPath(spendingAddress.HdPath)).GetWif(wallet.Network))
                .Send(script, amount)
                .SetChange(changeAddress.ScriptPubKey)
                .SendFees(fee)
                .BuildTransaction(true);

            if (!builder.Verify(tx))
            {
                throw new WalletException("Could not build transaction, please make sure you entered the correct data.");
            }

            return tx;
        }

        public Transaction CreateColdStakingWithdrawalTransaction(Wallet.Types.Wallet wallet, string password, HdAddress spendingAddress, PubKey destinationPubKey, Script changeScript, Money amount, Money fee)
        {
            TransactionOutputData spendingTransaction = wallet.walletStore.GetForAddress(spendingAddress.Address).ElementAt(0);
            var coin = new Coin(spendingTransaction.Id, (uint)spendingTransaction.Index, spendingTransaction.Amount, spendingTransaction.ScriptPubKey);

            Key privateKey = Key.Parse(wallet.EncryptedSeed, password, wallet.Network);

            Script script = destinationPubKey.ScriptPubKey;

            var builder = new TransactionBuilder(wallet.Network);
            builder.Extensions.Add(new ColdStakingBuilderExtension(false));
            Transaction tx = builder
                .AddCoins(new List<Coin> { coin })
                .AddKeys(new ExtKey(privateKey, wallet.ChainCode).Derive(new KeyPath(spendingAddress.HdPath)).GetWif(wallet.Network))
                .Send(script, amount)
                .SetChange(changeScript)
                .SendFees(fee)
                .BuildTransaction(true);

            if (!builder.Verify(tx))
            {
                throw new WalletException("Could not build transaction, please make sure you entered the correct data.");
            }

            return tx;
        }

        /// <summary>
        /// Creates a spendable transaction and spends its outputs to a cold staking script.
        /// Tests whether the original wallet, hot wallet and cold wallet record the expected information.
        /// </summary>
        [Fact]
        public void ProcessTransactionWithValidColdStakingSetupLoadsTransactionsIntoWalletIfMatching()
        {
            DataFolder dataFolder = CreateDataFolder(this);
            Directory.CreateDirectory(dataFolder.WalletPath);

            Wallet.Types.Wallet wallet = this.walletFixture.GenerateBlankWallet("myWallet", "password");
            (ExtKey ExtKey, string ExtPubKey) accountKeys = WalletTestsHelpers.GenerateAccountKeys(wallet, "password", "m/44'/0'/0'");

            (PubKey PubKey, BitcoinPubKeyAddress Address) spendingKeys = WalletTestsHelpers.GenerateAddressKeys(wallet, accountKeys.ExtPubKey, "0/0");
            (PubKey PubKey, BitcoinPubKeyAddress Address) changeKeys = WalletTestsHelpers.GenerateAddressKeys(wallet, accountKeys.ExtPubKey, "1/0");

            Wallet.Types.Wallet coldWallet = this.walletFixture.GenerateBlankWallet("myColdWallet", "password");
            (ExtKey ExtKey, string ExtPubKey) accountColdKeys = WalletTestsHelpers.GenerateAccountKeys(coldWallet, "password", $"m/44'/0'/{ColdStakingManager.ColdWalletAccountIndex}'");

            (PubKey PubKey, BitcoinPubKeyAddress Address) destinationColdKeys = WalletTestsHelpers.GenerateAddressKeys(coldWallet, accountColdKeys.ExtPubKey, "0/0");

            Wallet.Types.Wallet hotWallet = this.walletFixture.GenerateBlankWallet("myHotWallet", "password");
            (ExtKey ExtKey, string ExtPubKey) accountHotKeys = WalletTestsHelpers.GenerateAccountKeys(hotWallet, "password", $"m/44'/0'/{ColdStakingManager.HotWalletAccountIndex}'");

            (PubKey PubKey, BitcoinPubKeyAddress Address) destinationHotKeys = WalletTestsHelpers.GenerateAddressKeys(hotWallet, accountHotKeys.ExtPubKey, "0/0");

            var spendingAddress = new HdAddress
            {
                Index = 0,
                HdPath = $"m/44'/0'/0'/0/0",
                Address = spendingKeys.Address.ToString(),
                Pubkey = spendingKeys.PubKey.ScriptPubKey,
                ScriptPubKey = spendingKeys.Address.ScriptPubKey,
            };

            var destinationColdAddress = new HdAddress
            {
                Index = 0,
                HdPath = $"m/44'/0'/{ColdStakingManager.ColdWalletAccountIndex}'/0/0",
                Address = destinationColdKeys.Address.ToString(),
                Pubkey = destinationColdKeys.PubKey.ScriptPubKey,
                ScriptPubKey = destinationColdKeys.Address.ScriptPubKey,
            };

            var destinationHotAddress = new HdAddress
            {
                Index = 0,
                HdPath = $"m/44'/0'/{ColdStakingManager.HotWalletAccountIndex}'/0/0",
                Address = destinationHotKeys.Address.ToString(),
                Pubkey = destinationHotKeys.PubKey.ScriptPubKey,
                ScriptPubKey = destinationHotKeys.Address.ScriptPubKey,
            };

            var changeAddress = new HdAddress
            {
                Index = 0,
                HdPath = $"m/44'/0'/0'/1/0",
                Address = changeKeys.Address.ToString(),
                Pubkey = changeKeys.PubKey.ScriptPubKey,
                ScriptPubKey = changeKeys.Address.ScriptPubKey,
            };

            // Generate a spendable transaction
            (ChainIndexer chain, uint256 blockhash, Block block) chainInfo = WalletTestsHelpers.CreateChainAndCreateFirstBlockWithPaymentToAddress(wallet.Network, spendingAddress);
            TransactionOutputData spendingTransaction = WalletTestsHelpers.CreateTransactionDataFromFirstBlock(chainInfo);
            spendingTransaction.Address = spendingAddress.Address;
            wallet.walletStore.InsertOrUpdate(spendingTransaction);

            wallet.AccountsRoot.ElementAt(0).Accounts.Add(new HdAccount
            {
                Index = 0,
                Purpose = 44,
                Name = "account 0",
                HdPath = "m/44'/0'/0'",
                ExtendedPubKey = accountKeys.ExtPubKey,
                ExternalAddresses = new List<HdAddress> { spendingAddress },
                InternalAddresses = new List<HdAddress> { changeAddress }
            });

            coldWallet.AccountsRoot.ElementAt(0).Accounts.Add(new HdAccount
            {
                Index = ColdStakingManager.ColdWalletAccountIndex,
                Purpose = 44,
                Name = ColdStakingManager.ColdWalletAccountName,
                HdPath = $"m/44'/0'/{ColdStakingManager.ColdWalletAccountIndex}'",
                ExtendedPubKey = accountColdKeys.ExtPubKey,
                ExternalAddresses = new List<HdAddress> { destinationColdAddress },
                InternalAddresses = new List<HdAddress> { }
            });

            hotWallet.AccountsRoot.ElementAt(0).Accounts.Add(new HdAccount
            {
                Index = ColdStakingManager.HotWalletAccountIndex,
                Purpose = 44,
                Name = ColdStakingManager.HotWalletAccountName,
                HdPath = $"m/44'/0'/{ColdStakingManager.HotWalletAccountIndex}'",
                ExtendedPubKey = accountHotKeys.ExtPubKey,
                ExternalAddresses = new List<HdAddress> { destinationHotAddress },
                InternalAddresses = new List<HdAddress> { }
            });

            var walletFeePolicy = new Mock<IWalletFeePolicy>();
            walletFeePolicy.Setup(w => w.GetMinimumFee(258, 50))
                .Returns(new Money(5000));

            var walletSettings = new WalletSettings(new NodeSettings(network: this.Network));

            var coldWalletManager = new ColdStakingManager(this.Network, chainInfo.chain, walletSettings, dataFolder, walletFeePolicy.Object,
                new Mock<IAsyncProvider>().Object, new NodeLifetime(), new ScriptAddressReader(), this.LoggerFactory.Object, DateTimeProvider.Default, new Mock<ISignals>().Object, new Mock<IBroadcasterManager>().Object);
            coldWalletManager.Wallets.Add(wallet);
            coldWalletManager.Wallets.Add(coldWallet);
            coldWalletManager.LoadKeysLookup();

            // Create another instance for the hot wallet as it is not allowed to have both wallets on the same instance.
            var hotWalletManager = new ColdStakingManager(this.Network, chainInfo.chain, walletSettings, dataFolder, walletFeePolicy.Object,
                new Mock<IAsyncProvider>().Object, new NodeLifetime(), new ScriptAddressReader(), this.LoggerFactory.Object, DateTimeProvider.Default, new Mock<ISignals>().Object, new Mock<IBroadcasterManager>().Object);
            hotWalletManager.Wallets.Add(hotWallet);
            hotWalletManager.LoadKeysLookup();

            // Create a cold staking setup transaction.
            Transaction transaction = CreateColdStakingSetupTransaction(wallet, "password", spendingAddress, destinationColdKeys.PubKey, destinationHotKeys.PubKey,
                changeAddress, new Money(7500), new Money(5000));

            coldWalletManager.ProcessTransaction(transaction);
            hotWalletManager.ProcessTransaction(transaction);

            HdAddress spentAddressResult = wallet.AccountsRoot.ElementAt(0).Accounts.ElementAt(0).ExternalAddresses.ElementAt(0);
            Assert.Equal(1, wallet.walletStore.GetForAddress(spendingAddress.Address).Count());
            Assert.Equal(transaction.GetHash(), wallet.walletStore.GetForAddress(spentAddressResult.Address).ElementAt(0).SpendingDetails.TransactionId);
            Assert.Equal(transaction.Outputs[1].Value, wallet.walletStore.GetForAddress(spentAddressResult.Address).ElementAt(0).SpendingDetails.Payments.ElementAt(1).Amount);
            Assert.Equal(transaction.Outputs[1].ScriptPubKey, wallet.walletStore.GetForAddress(spentAddressResult.Address).ElementAt(0).SpendingDetails.Payments.ElementAt(1).DestinationScriptPubKey);

            Assert.Equal(1, wallet.walletStore.GetForAddress(wallet.AccountsRoot.ElementAt(0).Accounts.ElementAt(0).InternalAddresses.ElementAt(0).Address).Count());
            TransactionOutputData changeAddressResult = wallet.walletStore.GetForAddress(wallet.AccountsRoot.ElementAt(0).Accounts.ElementAt(0).InternalAddresses.ElementAt(0).Address).ElementAt(0);
            Assert.Equal(transaction.GetHash(), changeAddressResult.Id);
            Assert.Equal(transaction.Outputs[0].Value, changeAddressResult.Amount);
            Assert.Equal(transaction.Outputs[0].ScriptPubKey, changeAddressResult.ScriptPubKey);

            // Verify that the transaction has been recorded in the cold wallet.
            Assert.Equal(1, coldWallet.walletStore.GetForAddress(coldWallet.AccountsRoot.ElementAt(0).Accounts.ElementAt(0).ExternalAddresses.ElementAt(0).Address).Count());
            TransactionOutputData destinationColdAddressResult = coldWallet.walletStore.GetForAddress(coldWallet.AccountsRoot.ElementAt(0).Accounts.ElementAt(0).ExternalAddresses.ElementAt(0).Address).ElementAt(0);
            Assert.Equal(transaction.GetHash(), destinationColdAddressResult.Id);
            Assert.Equal(transaction.Outputs[1].Value, destinationColdAddressResult.Amount);
            Assert.Equal(transaction.Outputs[1].ScriptPubKey, destinationColdAddressResult.ScriptPubKey);

            // Verify that the transaction has been recorded in the hot wallet.
            Assert.Equal(1, hotWallet.walletStore.GetForAddress(hotWallet.AccountsRoot.ElementAt(0).Accounts.ElementAt(0).ExternalAddresses.ElementAt(0).Address).Count());
            TransactionOutputData destinationHotAddressResult = hotWallet.walletStore.GetForAddress(hotWallet.AccountsRoot.ElementAt(0).Accounts.ElementAt(0).ExternalAddresses.ElementAt(0).Address).ElementAt(0);
            Assert.Equal(transaction.GetHash(), destinationHotAddressResult.Id);
            Assert.Equal(transaction.Outputs[1].Value, destinationHotAddressResult.Amount);
            Assert.Equal(transaction.Outputs[1].ScriptPubKey, destinationHotAddressResult.ScriptPubKey);

            // Try withdrawing from the cold staking setup.
            Wallet.Types.Wallet withdrawalWallet = this.walletFixture.GenerateBlankWallet("myWithDrawalWallet", "password");
            (ExtKey ExtKey, string ExtPubKey) withdrawalAccountKeys = WalletTestsHelpers.GenerateAccountKeys(wallet, "password", "m/44'/0'/0'");

            (PubKey PubKey, BitcoinPubKeyAddress Address) withdrawalKeys = WalletTestsHelpers.GenerateAddressKeys(withdrawalWallet, withdrawalAccountKeys.ExtPubKey, "0/0");

            // Withdrawing to this address.
            var withdrawalAddress = new HdAddress
            {
                Index = 0,
                HdPath = $"m/44'/0'/0'/0/0",
                Address = withdrawalKeys.Address.ToString(),
                Pubkey = withdrawalKeys.PubKey.ScriptPubKey,
                ScriptPubKey = withdrawalKeys.Address.ScriptPubKey,
            };

            withdrawalWallet.AccountsRoot.ElementAt(0).Accounts.Add(new HdAccount
            {
                Index = 0,
                Name = "account 0",
                HdPath = "m/44'/0'/0'",
                ExtendedPubKey = accountKeys.ExtPubKey,
                ExternalAddresses = new List<HdAddress> { withdrawalAddress },
                InternalAddresses = new List<HdAddress> { }
            });

            // Will spend from the cold stake address and send the change back to the same address.
            var coldStakeAddress = coldWallet.AccountsRoot.ElementAt(0).Accounts.ElementAt(0).ExternalAddresses.ElementAt(0);
            Transaction withdrawalTransaction = CreateColdStakingWithdrawalTransaction(coldWallet, "password", coldStakeAddress,
                withdrawalKeys.PubKey, ColdStakingScriptTemplate.Instance.GenerateScriptPubKey(destinationColdKeys.PubKey.Hash, destinationHotKeys.PubKey.Hash),
                new Money(750), new Money(262));

            // Wallet manager for the wallet receiving the funds.
            var receivingWalletManager = new ColdStakingManager(this.Network, chainInfo.chain, walletSettings, dataFolder, walletFeePolicy.Object,
                new Mock<IAsyncProvider>().Object, new NodeLifetime(), new ScriptAddressReader(), this.LoggerFactory.Object, DateTimeProvider.Default, new Mock<ISignals>().Object, new Mock<IBroadcasterManager>().Object);
            receivingWalletManager.Wallets.Add(withdrawalWallet);
            receivingWalletManager.LoadKeysLookup();

            // Process the transaction in the cold wallet manager.
            coldWalletManager.ProcessTransaction(withdrawalTransaction);

            // Process the transaction in the hot wallet manager.
            hotWalletManager.ProcessTransaction(withdrawalTransaction);

            // Process the transaction in the receiving wallet manager.
            receivingWalletManager.ProcessTransaction(withdrawalTransaction);

            // Verify that the transaction has been recorded in the withdrawal wallet.
            Assert.Equal(1, withdrawalWallet.walletStore.GetForAddress(withdrawalWallet.AccountsRoot.ElementAt(0).Accounts.ElementAt(0).ExternalAddresses.ElementAt(0).Address).Count());
            TransactionOutputData withdrawalAddressResult = withdrawalWallet.walletStore.GetForAddress(withdrawalWallet.AccountsRoot.ElementAt(0).Accounts.ElementAt(0).ExternalAddresses.ElementAt(0).Address).First(x => x.Id == withdrawalTransaction.GetHash());
            Assert.Equal(withdrawalTransaction.GetHash(), withdrawalAddressResult.Id);
            Assert.Equal(withdrawalTransaction.Outputs[1].Value, withdrawalAddressResult.Amount);
            Assert.Equal(withdrawalTransaction.Outputs[1].ScriptPubKey, withdrawalAddressResult.ScriptPubKey);

            // Verify that the transaction has been recorded in the cold wallet.
            Assert.Equal(2, coldWallet.walletStore.GetForAddress(coldWallet.AccountsRoot.ElementAt(0).Accounts.ElementAt(0).ExternalAddresses.ElementAt(0).Address).Count());
            TransactionOutputData coldAddressResult = coldWallet.walletStore.GetForAddress(coldWallet.AccountsRoot.ElementAt(0).Accounts.ElementAt(0).ExternalAddresses.ElementAt(0).Address).First(x => x.Id == withdrawalTransaction.GetHash());
            Assert.Equal(withdrawalTransaction.GetHash(), coldAddressResult.Id);
            Assert.Equal(withdrawalTransaction.Outputs[0].Value, coldAddressResult.Amount);
            Assert.Equal(withdrawalTransaction.Outputs[0].ScriptPubKey, coldAddressResult.ScriptPubKey);

            // Verify that the transaction has been recorded in the hot wallet.
            Assert.Equal(2, hotWallet.walletStore.GetForAddress(hotWallet.AccountsRoot.ElementAt(0).Accounts.ElementAt(0).ExternalAddresses.ElementAt(0).Address).Count());
            TransactionOutputData hotAddressResult = hotWallet.walletStore.GetForAddress(hotWallet.AccountsRoot.ElementAt(0).Accounts.ElementAt(0).ExternalAddresses.ElementAt(0).Address).First(x => x.Id == withdrawalTransaction.GetHash());
            Assert.Equal(withdrawalTransaction.GetHash(), hotAddressResult.Id);
            Assert.Equal(withdrawalTransaction.Outputs[0].Value, hotAddressResult.Amount);
            Assert.Equal(withdrawalTransaction.Outputs[0].ScriptPubKey, hotAddressResult.ScriptPubKey);

            // Verify the hot amount returned by GetBalances.
            AccountBalance hotBalance = hotWalletManager.GetBalances("myHotWallet", ColdStakingManager.HotWalletAccountName).FirstOrDefault();
            Assert.Equal(hotBalance.AmountUnconfirmed, hotAddressResult.Amount);

            // Verify the cold amount returned by GetBalances.
            AccountBalance coldBalance = coldWalletManager.GetBalances("myColdWallet", ColdStakingManager.ColdWalletAccountName).FirstOrDefault();
            Assert.Equal(coldBalance.AmountUnconfirmed, coldAddressResult.Amount);
        }
    }
}