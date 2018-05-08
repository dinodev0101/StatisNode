﻿using NBitcoin;

namespace Stratis.Bitcoin.Features.Consensus
{
    /// <summary>
    /// An interface to read and write PoS information from store.
    /// </summary>
    public interface IStakeChain
    {
        /// <summary>
        /// Get the stake coresponding to the block.
        /// </summary>
        BlockStake Get(uint256 blockid);

        /// <summary>
        /// Set the stake for the given block header.
        /// </summary>
        void Set(ChainedHeader chainedHeader, BlockStake blockStake);
    }
}