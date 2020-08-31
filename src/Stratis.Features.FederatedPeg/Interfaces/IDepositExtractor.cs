﻿using System.Collections.Generic;
using NBitcoin;
using Stratis.Bitcoin.Primitives;
using Stratis.Features.FederatedPeg.Models;
using Stratis.Features.FederatedPeg.SourceChain;

namespace Stratis.Features.FederatedPeg.Interfaces
{
    /// <summary>
    /// This component is responsible for finding all deposits made to a given address
    /// in a given block, find out if they should trigger a cross chain transfer, and if so
    /// extracting the transfers' details.
    /// </summary>
    public interface IDepositExtractor
    {
        /// <summary>
        /// Extracts deposits from the transactions in a block.
        /// </summary>
        /// <param name="block">The block containing the transactions.</param>
        /// <param name="blockHeight">The height of the block containing the transactions.</param>
        /// <param name="depositRetrievalType">The type of retrieval to perform. The type will determine the value of the deposit to be processed.</param>
        /// <returns>The extracted deposit (if any), otherwise returns an empty list.</returns>
        IReadOnlyList<IDeposit> ExtractDepositsFromBlock(Block block, int blockHeight, DepositRetrievalType depositRetrievalType);

        /// <summary>
        /// Extracts a deposit from a transaction (if any).
        /// </summary>
        /// <param name="transaction">The transaction to extract deposits from.</param>
        /// <param name="blockHeight">The block height of the block containing the transaction.</param>
        /// <param name="blockHash">The block hash of the block containing the transaction.</param>
        /// <param name="depositRetrievalType">The type of retrieval to perform. The type will determine the value of the deposit to be processed.</param>
        /// <returns>The extracted deposit (if any), otherwise <c>null</c>.</returns>
        IDeposit ExtractDepositFromTransaction(Transaction transaction, int blockHeight, uint256 blockHash, DepositRetrievalType depositRetrievalType);

        /// <summary>
        /// Gets deposits from the newly matured block.
        /// </summary>
        /// <param name="newlyMaturedBlock">The newly matured block.</param>
        /// <param name="depositRetrievalType">The type of retrieval to perform. The type will determine the value of the deposit to be processed.</param>
        /// <returns>The matured deposits.</returns>
        MaturedBlockDepositsModel ExtractBlockDeposits(ChainedHeaderBlock newlyMaturedBlock, DepositRetrievalType depositRetrievalType);
    }
}
