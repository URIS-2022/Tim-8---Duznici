﻿using System;
using System.IO;
using System.Linq;
using Blockcore.Consensus.TransactionInfo;
using Blockcore.Features.Wallet;
using Blockcore.Features.Wallet.Api.Controllers;
using Blockcore.Features.Wallet.Api.Models;
using Blockcore.Features.Wallet.Exceptions;
using Blockcore.Features.Wallet.Types;
using Blockcore.IntegrationTests.Common;
using Blockcore.IntegrationTests.Common.EnvironmentMockUpHelpers;
using Blockcore.IntegrationTests.Common.Extensions;
using Blockcore.IntegrationTests.Common.ReadyData;
using Blockcore.Networks;
using Blockcore.Networks.Bitcoin;
using Blockcore.Tests.Common;
using Blockcore.Tests.Common.TestFramework;
using FluentAssertions;
using NBitcoin;
using Xunit.Abstractions;

namespace Blockcore.IntegrationTests.Wallet
{
    public partial class SendingTransactionOverPolicyByteLimit : BddSpecification
    {
        private NodeBuilder nodeBuilder;
        private Network network;
        private CoreNode firstNode;
        private CoreNode secondNode;
        private TransactionBuildContext transactionBuildContext;

        private Transaction transaction;

        private Exception caughtException;
        private const string WalletName = "mywallet";
        private const string WalletPassword = "password";
        private const string WalletAccountName = "account 0";

        public SendingTransactionOverPolicyByteLimit(ITestOutputHelper outputHelper) : base(outputHelper)
        {
        }

        protected override void BeforeTest()
        {
            this.network = new BitcoinRegTest();
            this.nodeBuilder = NodeBuilder.Create(Path.Combine(GetType().Name, this.CurrentTest.DisplayName));
        }

        protected override void AfterTest()
        {
            this.nodeBuilder.Dispose();
        }

        private void two_connected_nodes()
        {
            this.firstNode = this.nodeBuilder.CreateStratisPowNode(this.network).WithReadyBlockchainData(ReadyBlockchain.BitcoinRegTest100Miner).Start();
            this.secondNode = this.nodeBuilder.CreateStratisPowNode(this.network).WithWallet().Start();

            TestHelper.Connect(this.firstNode, this.secondNode);
        }

        private void node1_builds_undersize_transaction_to_send_to_node2()
        {
            Node1BuildsTransactionToSendToNode2(2650);
        }

        private void serialized_size_of_transaction_is_close_to_upper_limit()
        {
            this.transaction.GetSerializedSize().Should().BeInRange(95000, 100000);
        }

        private void mempool_of_receiver_node2_is_empty()
        {
            this.secondNode.FullNode.MempoolManager().GetMempoolAsync().Result.Should().BeEmpty();
        }

        private void mempool_of_node2_has_received_transaction()
        {
            TestBase.WaitLoop(() => this.firstNode.FullNode.MempoolManager().GetMempoolAsync().Result.Any());
            this.firstNode.FullNode.MempoolManager().GetMempoolAsync().Result.Should().Contain(this.transaction.GetHash());
        }

        private void node1_builds_oversize_tx_to_send_to_node2()
        {
            Node1BuildsTransactionToSendToNode2(2900);
        }

        private void sending_the_transaction()
        {
            this.firstNode.FullNode.NodeController<WalletController>().SendTransaction(new SendTransactionRequest(this.transaction.ToHex(this.firstNode.FullNode.Network.Consensus.ConsensusFactory)));
        }

        private void Node1BuildsTransactionToSendToNode2(int txoutputs)
        {
            TestHelper.MineBlocks(this.firstNode, 100);

            var nodeTwoAddresses = this.secondNode.FullNode.WalletManager().GetUnusedAddresses(new WalletAccountReference(WalletName, WalletAccountName), txoutputs);

            var nodeTwoRecipients = nodeTwoAddresses.Select(address => new Recipient
            {
                ScriptPubKey = address.ScriptPubKey,
                Amount = Money.COIN
            }).ToList();

            this.transactionBuildContext = TestHelper.CreateTransactionBuildContext(this.firstNode.FullNode.Network, WalletName, WalletAccountName, WalletPassword, nodeTwoRecipients, FeeType.Medium, 101);

            try
            {
                this.transaction = this.firstNode.FullNode.WalletTransactionHandler().BuildTransaction(this.transactionBuildContext);
                Money transactionFee = this.firstNode.FullNode.WalletTransactionHandler().EstimateFee(this.transactionBuildContext);
            }
            catch (Exception e)
            {
                this.caughtException = e;
            }
        }

        private void node1_fails_with_oversize_transaction_wallet_error()
        {
            this.caughtException.Should().BeOfType<WalletException>().Which.Message.Should().Contain("Transaction's size is too high");
        }

        private void node1_wallet_throws_no_exceptions()
        {
            this.caughtException.Should().BeNull();
        }
    }
}