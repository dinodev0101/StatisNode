﻿using NBitcoin;
using Stratis.Bitcoin.Primitives;
using Stratis.Bitcoin.Signals;
using Stratis.Bitcoin.Utilities;
using Stratis.FederatedPeg.Features.FederationGateway.Interfaces;
using Stratis.FederatedPeg.Features.FederationGateway.Models;

namespace Stratis.FederatedPeg.Features.FederationGateway.Notifications
{
    /// <summary>
    /// Observer that passes notifications indicating the arrival of new <see cref="Block"/>s
    /// onto the CrossChainTransactionMonitor.
    /// </summary>
    public class BlockObserver : SignalObserver<ChainedHeaderBlock>
    {
        // The monitor we pass the new blocks onto.
        private readonly ICrossChainTransactionMonitor crossChainTransactionMonitor;

        private readonly IFederationWalletSyncManager walletSyncManager;

        private readonly IMaturedBlockSender maturedBlockSender;

        private readonly IDepositExtractor depositExtractor;

        private readonly IWithdrawalExtractor withdrawalExtractor;

        private readonly IBlockTipSender blockTipSender;

        private readonly ConcurrentChain chain;

        /// <summary>
        /// Initialize the block observer with the wallet manager and the cross chain monitor.
        /// </summary>
        /// <param name="walletSyncManager">The wallet sync manager to pass new incoming blocks to.</param>
        /// <param name="crossChainTransactionMonitor">The cross-chain transaction monitor to pass new incoming blocks to.</param>
        /// <param name="depositExtractor">The component used to extract the deposits from the blocks appearing on chain.</param>
        /// <param name="maturedBlockSender">Service responsible for publishing newly matured blocks.</param>
        /// <param name="blockTipSender">Service responsible for publishing the block tip.</param>
        public BlockObserver(IFederationWalletSyncManager walletSyncManager,
                             ICrossChainTransactionMonitor crossChainTransactionMonitor,
                             IDepositExtractor depositExtractor,
                             IWithdrawalExtractor withdrawalExtractor,
                             IMaturedBlockSender maturedBlockSender,
                             IBlockTipSender blockTipSender)
        {
            Guard.NotNull(walletSyncManager, nameof(walletSyncManager));
            Guard.NotNull(crossChainTransactionMonitor, nameof(crossChainTransactionMonitor));
            Guard.NotNull(maturedBlockSender, nameof(maturedBlockSender));
            Guard.NotNull(blockTipSender, nameof(blockTipSender));
            Guard.NotNull(depositExtractor, nameof(depositExtractor));
            Guard.NotNull(withdrawalExtractor, nameof(withdrawalExtractor));

            this.walletSyncManager = walletSyncManager;
            this.crossChainTransactionMonitor = crossChainTransactionMonitor;
            this.maturedBlockSender = maturedBlockSender;
            this.depositExtractor = depositExtractor;
            this.withdrawalExtractor = withdrawalExtractor;
            this.blockTipSender = blockTipSender;
        }

        /// <summary>
        /// When a block is received it is passed to the monitor.
        /// </summary>
        /// <param name="chainedHeaderBlock">The new block.</param>
        protected override void OnNextCore(ChainedHeaderBlock chainedHeaderBlock)
        {
            this.crossChainTransactionMonitor.ProcessBlock(chainedHeaderBlock.Block);

            this.walletSyncManager.ProcessBlock(chainedHeaderBlock.Block);

            this.blockTipSender.SendBlockTipAsync(
                new BlockTipModel(chainedHeaderBlock.ChainedHeader.HashBlock, chainedHeaderBlock.ChainedHeader.Height));

            var withdrawals = this.withdrawalExtractor.ExtractWithdrawalsFromBlock(
                chainedHeaderBlock.Block,
                chainedHeaderBlock.ChainedHeader.Height);
            //todo add that to the store or change status of pending transfers to on chain

            // todo: persist the last seen block height in database
            IMaturedBlockDeposits maturedBlockDeposits = 
                this.depositExtractor.ExtractMaturedBlockDeposits(chainedHeaderBlock.ChainedHeader);

            if (maturedBlockDeposits == null) return;

            this.maturedBlockSender.SendMaturedBlockDepositsAsync(maturedBlockDeposits).ConfigureAwait(false);
        }
    }
}