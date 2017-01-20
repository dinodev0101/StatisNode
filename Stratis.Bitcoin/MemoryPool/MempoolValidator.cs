﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.BitcoinCore;
using NBitcoin.Protocol;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Consensus;

namespace Stratis.Bitcoin.MemoryPool
{
	public class MempoolValidator
	{
		public const int SERIALIZE_TRANSACTION_NO_WITNESS = 0x40000000;

		public const int WITNESS_SCALE_FACTOR = 4;

		// Default for -maxmempool, maximum megabytes of mempool memory usage 
		public const int DEFAULT_MAX_MEMPOOL_SIZE = 300;
		public const bool DEFAULT_RELAYPRIORITY = true;
		// Default for -minrelaytxfee, minimum relay fee for transactions 
		public const int DEFAULT_MIN_RELAY_TX_FEE = 1000;
		public const int DEFAULT_LIMITFREERELAY = 0;

		/** Default for -limitancestorcount, max number of in-mempool ancestors */
		public const int DEFAULT_ANCESTOR_LIMIT = 25;
		/** Default for -limitancestorsize, maximum kilobytes of tx + all in-mempool ancestors */
		public const int DEFAULT_ANCESTOR_SIZE_LIMIT = 101;
		/** Default for -limitdescendantcount, max number of in-mempool descendants */
		public const int DEFAULT_DESCENDANT_LIMIT = 25;
		/** Default for -limitdescendantsize, maximum kilobytes of in-mempool descendants */
		public const int DEFAULT_DESCENDANT_SIZE_LIMIT = 101;
		/** Default for -mempoolexpiry, expiration time for mempool transactions in hours */
		public const int DEFAULT_MEMPOOL_EXPIRY = 336;

		private readonly SchedulerPairSession mempoolScheduler;
		private readonly TxMemPool.DateTimeProvider dateTimeProvider;
		private readonly NodeArgs nodeArgs;
		private readonly ConcurrentChain chain;
		private readonly CachedCoinView cachedCoinView;
		private readonly TxMemPool memPool;
		private readonly ConsensusValidator consensusValidator;

		private bool fEnableReplacement = true;
		private bool fRequireStandard = true;
		private static readonly FeeRate MinRelayTxFee = new FeeRate(DEFAULT_MIN_RELAY_TX_FEE);
		private FreeLimiterSection FreeLimiter;

		private class FreeLimiterSection
		{
			public double FreeCount;
			public long LastTime;
		}

		public MempoolValidator(TxMemPool memPool, SchedulerPairSession mempoolScheduler,
			ConsensusValidator consensusValidator, TxMemPool.DateTimeProvider dateTimeProvider, NodeArgs nodeArgs,
			ConcurrentChain chain, CachedCoinView cachedCoinView)
		{
			this.memPool = memPool;
			this.mempoolScheduler = mempoolScheduler;
			this.consensusValidator = consensusValidator;
			this.dateTimeProvider = dateTimeProvider;
			this.nodeArgs = nodeArgs;
			this.chain = chain;
			this.cachedCoinView = cachedCoinView;

			this.fRequireStandard = !(nodeArgs.RegTest || nodeArgs.Testnet);
			FreeLimiter = new FreeLimiterSection();
		}

		public async Task<bool> AcceptToMemoryPoolWithTime(MemepoolValidationState state, Transaction tx,
			bool fLimitFree, long nAcceptTime, bool fOverrideMempoolLimit, Money nAbsurdFee)
		{
			try
			{
				List<uint256> vHashTxToUncache = new List<uint256>();
				await this.AcceptToMemoryPoolWorker(state, tx, fLimitFree, nAcceptTime, fOverrideMempoolLimit, nAbsurdFee,
						vHashTxToUncache);
				//if (!res) {
				//    BOOST_FOREACH(const uint256& hashTx, vHashTxToUncache)
				//        pcoinsTip->Uncache(hashTx);
				//}
				return true;
			}
			catch (ConsensusErrorException consensusError)
			{
				state.Error = new MemepoolError(consensusError.ConsensusError);
				return false;
			}

			// After we've (potentially) uncached entries, ensure our coins cache is still within its size limits
			//ValidationState stateDummy;
			//FlushStateToDisk(stateDummy, FLUSH_STATE_PERIODIC);
		}

		public Task<bool> AcceptToMemoryPool(MemepoolValidationState state, Transaction tx, bool fLimitFree,
			bool fOverrideMempoolLimit, Money nAbsurdFee)
		{
			return AcceptToMemoryPoolWithTime(state, tx, fLimitFree, this.dateTimeProvider.GetTime(), fOverrideMempoolLimit,
				nAbsurdFee);
		}

		// Check for conflicts with in-memory transactions
		private void CheckConflicts(MempoolValidationContext context)
		{
			context.SetConflicts = new List<uint256>();

			//LOCK(pool.cs); // protect pool.mapNextTx
			foreach (var txin in context.Transaction.Inputs)
			{
				var itConflicting = this.memPool.MapNextTx.Find(f => f.OutPoint == txin.PrevOut);
				if (itConflicting != null)
				{
					var ptxConflicting = itConflicting.Transaction;
					if (!context.SetConflicts.Contains(ptxConflicting.GetHash()))
					{
						// Allow opt-out of transaction replacement by setting
						// nSequence >= maxint-1 on all inputs.
						//
						// maxint-1 is picked to still allow use of nLockTime by
						// non-replaceable transactions. All inputs rather than just one
						// is for the sake of multi-party protocols, where we don't
						// want a single party to be able to disable replacement.
						//
						// The opt-out ignores descendants as anyone relying on
						// first-seen mempool behavior should be checking all
						// unconfirmed ancestors anyway; doing otherwise is hopelessly
						// insecure.
						bool fReplacementOptOut = true;
						if (fEnableReplacement)
						{
							foreach (var txiner in ptxConflicting.Inputs)

							{
								if (txiner.Sequence < Sequence.Final - 1)
								{
									fReplacementOptOut = false;
									break;
								}
							}
						}

						if (fReplacementOptOut)
							context.State.Fail(new MemepoolError("txn-mempool-conflict")).Throw();

						context.SetConflicts.Add(ptxConflicting.GetHash());
					}
				}
			}
		}

		private async Task AcceptToMemoryPoolWorker(MemepoolValidationState state, Transaction tx, bool fLimitFree,
			long nAcceptTime, bool fOverrideMempoolLimit, Money nAbsurdFee, List<uint256> vHashTxnToUncache)
		{
			var context = new MempoolValidationContext(tx, state);

			// any access to mempool has to be behind a scheduler
			// the following code only reads from mempool 
			// so we use the concurrent scheduler
			await this.mempoolScheduler.DoConcurrent(async () =>
			{
				// state filled in by CheckTransaction
				this.consensusValidator.CheckTransaction(tx);

				// Coinbase is only valid in a block, not as a loose transaction
				if (tx.IsCoinBase)
					state.Fail(new MemepoolError("coinbase")).Throw(); //.DoS(100, false, REJECT_INVALID, "coinbase");

				// TODO: IsWitnessEnabled
				//// Reject transactions with witness before segregated witness activates (override with -prematurewitness)
				//bool witnessEnabled = IsWitnessEnabled(chainActive.Tip(), Params().GetConsensus());
				//if (!GetBoolArg("-prematurewitness",false) && tx.HasWitness() && !witnessEnabled) {
				//    return state.DoS(0, false, REJECT_NONSTANDARD, "no-witness-yet", true);
				//}

				// Rather not work on nonstandard transactions (unless -testnet/-regtest)
				if (this.fRequireStandard)
				{
					var errors = new NBitcoin.Policy.StandardTransactionPolicy().Check(tx, null);
					if (errors.Any())
						state.Fail(new MemepoolError(MemepoolErrors.REJECT_NONSTANDARD, errors.First().ToString())).Throw();
				}

				// Only accept nLockTime-using transactions that can be mined in the next
				// block; we don't want our mempool filled up with transactions that can't
				// be mined yet.
				if (!CheckFinalTx(tx, ConsensusValidator.StandardLocktimeVerifyFlags))
					state.Fail(new MemepoolError(MemepoolErrors.REJECT_NONSTANDARD, "non final transaction")).Throw();

				// is it already in the memory pool?
				if (this.memPool.Exists(context.TransactionHash))
					state.Fail(new MemepoolError(MemepoolErrors.REJECT_ALREADY_KNOWN, "txn-already-in-mempool")).Throw();

				// Check for conflicts with in-memory transactions
				this.CheckConflicts(context);

				// create the MemPoolCoinView and load relevant utxoset
				context.View = new MemPoolCoinView(this.cachedCoinView, this.memPool);
				await context.View.LoadView(tx);

				Money nValueIn = 0;
				LockPoints lp = new LockPoints();

				// do we already have it?
				//bool fHadTxInCache = pcoinsTip->HaveCoinsInCache(hash);

				if (context.View.HaveCoins(context.TransactionHash))
				{
					//if (!fHadTxInCache)
					//	vHashTxnToUncache.push_back(hash);
					state.Fail(new MemepoolError(MemepoolErrors.REJECT_ALREADY_KNOWN, "txn-already-known")).Throw();
				}

				// do all inputs exist?
				// Note that this does not check for the presence of actual outputs (see the next check for that),
				// and only helps with filling in pfMissingInputs (to determine missing vs spent).
				foreach (var txin in tx.Inputs)
				{
					//if (!pcoinsTip->HaveCoinsInCache(txin.prevout.hash))
					//	vHashTxnToUncache.push_back(txin.prevout.hash);
					if (!context.View.HaveCoins(txin.PrevOut.Hash))
					{
						state.MissingInputs = true;
						return; // fMissingInputs and !state.IsInvalid() is used to detect this condition, don't set state.Invalid()
					}
				}

				// are the actual inputs available?
				if (!context.View.HaveInputs(tx))
					state.Fail(new MemepoolError(MemepoolErrors.REJECT_DUPLICATE, "bad-txns-inputs-spent")).Throw();

				// Bring the best block into scope
				//view.GetBestBlock();

				nValueIn = context.View.GetValueIn(tx);

				// we have all inputs cached now, so switch back to dummy, so we don't need to keep lock on mempool
				//view.SetBackend(dummy);

				// Only accept BIP68 sequence locked transactions that can be mined in the next
				// block; we don't want our mempool filled up with transactions that can't
				// be mined yet.
				// Must keep pool.cs for this unless we change CheckSequenceLocks to take a
				// CoinsViewCache instead of create its own
				if (!CheckSequenceLocks(tx, ConsensusValidator.StandardLocktimeVerifyFlags, lp))
					state.Fail(new MemepoolError(MemepoolErrors.REJECT_NONSTANDARD, "non-BIP68-final")).Throw();

				// Check for non-standard pay-to-script-hash in inputs
				if (fRequireStandard && !this.AreInputsStandard(tx, context.View))
					state.Fail(new MemepoolError(MemepoolErrors.REJECT_NONSTANDARD, "bad-txns-nonstandard-inputs")).Throw();

				// Check for non-standard witness in P2WSH
				if (tx.HasWitness && fRequireStandard && !IsWitnessStandard(tx, context.View))
					state.Fail(new MemepoolError(MemepoolErrors.REJECT_NONSTANDARD, "bad-witness-nonstandard")).Throw();

				var nSigOpsCost = consensusValidator.GetTransactionSigOpCost(tx, context.View.Set,
					new ConsensusFlags {ScriptFlags = ScriptVerify.Standard});

				Money nValueOut = tx.TotalOut;
				Money nFees = nValueIn - nValueOut;
				// nModifiedFees includes any fee deltas from PrioritiseTransaction
				Money nModifiedFees = nFees;
				double nPriorityDummy = 0;
				this.memPool.ApplyDeltas(context.TransactionHash, ref nPriorityDummy, ref nModifiedFees);
				context.ModifiedFees = nModifiedFees;

				Money inChainInputValue = Money.Zero;
				double dPriority = context.View.GetPriority(tx, this.chain.Height, inChainInputValue);

				// Keep track of transactions that spend a coinbase, which we re-scan
				// during reorgs to ensure COINBASE_MATURITY is still met.
				bool fSpendsCoinbase = false;
				foreach (var txInput in tx.Inputs)
				{
					var coins = context.View.Set.AccessCoins(txInput.PrevOut.Hash);
					if (coins.IsCoinbase)
					{
						fSpendsCoinbase = true;
						break;
					}
				}

				context.Entry = new TxMemPoolEntry(tx, nFees, nAcceptTime, dPriority, this.chain.Height, inChainInputValue,
					fSpendsCoinbase, nSigOpsCost, lp);
				context.EntrySize = (int) context.Entry.GetTxSize();

				// Check that the transaction doesn't have an excessive number of
				// sigops, making it impossible to mine. Since the coinbase transaction
				// itself can contain sigops MAX_STANDARD_TX_SIGOPS is less than
				// MAX_BLOCK_SIGOPS; we still consider this an invalid rather than
				// merely non-standard transaction.
				if (nSigOpsCost > ConsensusValidator.MAX_BLOCK_SIGOPS_COST)
					state.Fail(new MemepoolError(MemepoolErrors.REJECT_NONSTANDARD, "bad-txns-too-many-sigops")).Throw();

				Money mempoolRejectFee = this.memPool.GetMinFee(this.nodeArgs.Mempool.MaxMempool*1000000).GetFee(context.EntrySize);
				if (mempoolRejectFee > 0 && context.ModifiedFees < mempoolRejectFee)
				{
					state.Fail(new MemepoolError(MemepoolErrors.REJECT_INSUFFICIENTFEE,
						$"mempool-min-fee-not-met {nFees} < {mempoolRejectFee}")).Throw();

				}
				else if (nodeArgs.Mempool.RelayPriority && context.ModifiedFees < MinRelayTxFee.GetFee(context.EntrySize) &&
				         !TxMemPool.AllowFree(context.Entry.GetPriority(this.chain.Height + 1)))
				{
					// Require that free transactions have sufficient priority to be mined in the next block.
					state.Fail(new MemepoolError(MemepoolErrors.REJECT_INSUFFICIENTFEE, "insufficient priority")).Throw();
				}

				// Continuously rate-limit free (really, very-low-fee) transactions
				// This mitigates 'penny-flooding' -- sending thousands of free transactions just to
				// be annoying or make others' transactions take longer to confirm.
				if (fLimitFree && context.ModifiedFees < MinRelayTxFee.GetFee(context.EntrySize))
				{
					// todo: move this code to be called later in its own exclusive scheduler

					var nNow = this.dateTimeProvider.GetTime();
					//LOCK(csFreeLimiter);

					// Use an exponentially decaying ~10-minute window:

					this.FreeLimiter.FreeCount *= Math.Pow(1.0 - 1.0/600.0, (double) (nNow - this.FreeLimiter.LastTime));
					this.FreeLimiter.LastTime = nNow;
					// -limitfreerelay unit is thousand-bytes-per-minute
					// At default rate it would take over a month to fill 1GB
					if (this.FreeLimiter.FreeCount + context.EntrySize >= this.nodeArgs.Mempool.LimitFreeRelay*10*1000)
						state.Fail(new MemepoolError(MemepoolErrors.REJECT_INSUFFICIENTFEE, "rate limited free transaction")).Throw();

					Logging.Logs.Mempool.LogInformation(
						$"Rate limit dFreeCount: {this.FreeLimiter.FreeCount} => {this.FreeLimiter.FreeCount + context.EntrySize}");
					this.FreeLimiter.FreeCount += context.EntrySize;
				}

				if (nAbsurdFee != null && nFees > nAbsurdFee)
					state.Fail(new MemepoolError(MemepoolErrors.REJECT_HIGHFEE, $"absurdly-high-fee {nFees} > {nAbsurdFee} ")).Throw();

				// Calculate in-mempool ancestors, up to a limit.
				context.SetAncestors = new TxMemPool.SetEntries();
				var nLimitAncestors = nodeArgs.Mempool.LimitAncestors;
				var nLimitAncestorSize = nodeArgs.Mempool.LimitAncestorSize*1000;
				var nLimitDescendants = nodeArgs.Mempool.LimitDescendants;
				var nLimitDescendantSize = nodeArgs.Mempool.LimitDescendantSize*1000;
				string errString;
				if (!this.memPool.CalculateMemPoolAncestors(context.Entry, context.SetAncestors, nLimitAncestors, 
					nLimitAncestorSize, nLimitDescendants, nLimitDescendantSize, out errString))
				{
					state.Fail(new MemepoolError(MemepoolErrors.REJECT_NONSTANDARD, "too-long-mempool-chain", errString)).Throw();
				}

				// A transaction that spends outputs that would be replaced by it is invalid. Now
				// that we have the set of all ancestors we can detect this
				// pathological case by making sure setConflicts and setAncestors don't
				// intersect.
				foreach (var ancestorIt in context.SetAncestors)
				{
					var hashAncestor = ancestorIt.TransactionHash;
					if (context.SetConflicts.Contains(hashAncestor))
					{
						state.Fail(new MemepoolError(MemepoolErrors.REJECT_INVALID, "bad-txns-spends-conflicting-tx",
							$"{context.TransactionHash} spends conflicting transaction {hashAncestor}")).Throw();
					}
				}



				// Check if it's economically rational to mine this transaction rather
				// than the ones it replaces.
				context.ConflictingFees = 0;
				context.ConflictingSize = 0;
				context.ConflictingCount = 0;
				context.AllConflicting = new TxMemPool.SetEntries();

				// If we don't hold the lock allConflicting might be incomplete; the
				// subsequent RemoveStaged() and addUnchecked() calls don't guarantee
				// mempool consistency for us.
				//LOCK(pool.cs);
				if (context.SetConflicts.Any())
				{
					FeeRate newFeeRate = new FeeRate(context.ModifiedFees, context.EntrySize);
					List<uint256> setConflictsParents = new List<uint256>();
					const int maxDescendantsToVisit = 100;
					TxMemPool.SetEntries setIterConflicting = new TxMemPool.SetEntries();
					foreach (var hashConflicting in context.SetConflicts)
					{
						var mi = this.memPool.MapTx.TryGet(hashConflicting);
						if (mi == null)
							continue;

						// Save these to avoid repeated lookups
						setIterConflicting.Add(mi);

						// Don't allow the replacement to reduce the feerate of the
						// mempool.
						//
						// We usually don't want to accept replacements with lower
						// feerates than what they replaced as that would lower the
						// feerate of the next block. Requiring that the feerate always
						// be increased is also an easy-to-reason about way to prevent
						// DoS attacks via replacements.
						//
						// The mining code doesn't (currently) take children into
						// account (CPFP) so we only consider the feerates of
						// transactions being directly replaced, not their indirect
						// descendants. While that does mean high feerate children are
						// ignored when deciding whether or not to replace, we do
						// require the replacement to pay more overall fees too,
						// mitigating most cases.
						FeeRate oldFeeRate = new FeeRate(mi.ModifiedFee, (int) mi.GetTxSize());
						if (newFeeRate <= oldFeeRate)
						{
							state.Fail(new MemepoolError(MemepoolErrors.REJECT_INSUFFICIENTFEE, "insufficient-fee",
									$"rejecting replacement {context.TransactionHash}; new feerate {newFeeRate} <= old feerate {oldFeeRate}"))
								.Throw();
						}

						foreach (var txin in mi.Transaction.Inputs)
						{
							setConflictsParents.Add(txin.PrevOut.Hash);
						}

						context.ConflictingCount += mi.CountWithDescendants;
					}
					// This potentially overestimates the number of actual descendants
					// but we just want to be conservative to avoid doing too much
					// work.
					if (context.ConflictingCount <= maxDescendantsToVisit)
					{
						// If not too many to replace, then calculate the set of
						// transactions that would have to be evicted
						foreach (var it in setIterConflicting)
						{
							this.memPool.CalculateDescendants(it, context.AllConflicting);
						}
						foreach (var it in context.AllConflicting)
						{
							context.ConflictingFees += it.ModifiedFee;
							context.ConflictingSize += it.GetTxSize();
						}
					}
					else
					{
						state.Fail(new MemepoolError(MemepoolErrors.REJECT_NONSTANDARD, "too many potential replacements",
								$"rejecting replacement {context.TransactionHash}; too many potential replacements ({context.ConflictingCount} > {maxDescendantsToVisit})"))
							.Throw();
					}

					for (int j = 0; j < context.Transaction.Inputs.Count; j++)
					{
						// We don't want to accept replacements that require low
						// feerate junk to be mined first. Ideally we'd keep track of
						// the ancestor feerates and make the decision based on that,
						// but for now requiring all new inputs to be confirmed works.
						if (!setConflictsParents.Contains(context.Transaction.Inputs[j].PrevOut.Hash))
						{
							// Rather than check the UTXO set - potentially expensive -
							// it's cheaper to just check if the new input refers to a
							// tx that's in the mempool.
							if (this.memPool.MapTx.ContainsKey(context.Transaction.Inputs[j].PrevOut.Hash))
								state.Fail(new MemepoolError(MemepoolErrors.REJECT_NONSTANDARD, "replacement-adds-unconfirmed",
									$"replacement {context.TransactionHash} adds unconfirmed input, idx {j}")).Throw();
						}
					}

					// The replacement must pay greater fees than the transactions it
					// replaces - if we did the bandwidth used by those conflicting
					// transactions would not be paid for.
					if (context.ModifiedFees < context.ConflictingFees)
					{
						state.Fail(new MemepoolError(MemepoolErrors.REJECT_INSUFFICIENTFEE, "insufficient-fee",
								$"rejecting replacement {context.TransactionHash}, less fees than conflicting txs; {context.ModifiedFees} < {context.ConflictingFees}"))
							.Throw();
					}

					// Finally in addition to paying more fees than the conflicts the
					// new transaction must pay for its own bandwidth.
					Money nDeltaFees = context.ModifiedFees - context.ConflictingFees;
					if (nDeltaFees < MinRelayTxFee.GetFee(context.EntrySize))
					{
						state.Fail(new MemepoolError(MemepoolErrors.REJECT_INSUFFICIENTFEE, "insufficient-fee",
								$"rejecting replacement {context.TransactionHash}, not enough additional fees to relay; {nDeltaFees} < {MinRelayTxFee.GetFee(context.EntrySize)}"))
							.Throw();
					}

				}


				var scriptVerifyFlags = ScriptVerify.Standard;
				if (!this.fRequireStandard)
				{
					// TODO: implement -promiscuousmempoolflags
					// scriptVerifyFlags = GetArg("-promiscuousmempoolflags", scriptVerifyFlags);
				}

				// Check against previous transactions
				// This is done last to help prevent CPU exhaustion denial-of-service attacks.
				PrecomputedTransactionData txdata = new PrecomputedTransactionData(tx);
				if (!CheckInputs(context, scriptVerifyFlags, txdata))
				{
					// TODO: implement witness checks
					//// SCRIPT_VERIFY_CLEANSTACK requires SCRIPT_VERIFY_WITNESS, so we
					//// need to turn both off, and compare against just turning off CLEANSTACK
					//// to see if the failure is specifically due to witness validation.
					//if (!tx.HasWitness() && CheckInputs(tx, state, view, true, scriptVerifyFlags & ~(SCRIPT_VERIFY_WITNESS | SCRIPT_VERIFY_CLEANSTACK), true, txdata) &&
					//	!CheckInputs(tx, state, view, true, scriptVerifyFlags & ~SCRIPT_VERIFY_CLEANSTACK, true, txdata))
					//{
					//	// Only the witness is missing, so the transaction itself may be fine.
					//	state.SetCorruptionPossible();
					//}

					state.Fail(new MemepoolError("check inputs")).Throw();
				}

				// Check again against just the consensus-critical mandatory script
				// verification flags, in case of bugs in the standard flags that cause
				// transactions to pass as valid when they're actually invalid. For
				// instance the STRICTENC flag was incorrectly allowing certain
				// CHECKSIG NOT scripts to pass, even though they were invalid.
				//
				// There is a similar check in CreateNewBlock() to prevent creating
				// invalid blocks, however allowing such transactions into the mempool
				// can be exploited as a DoS attack.
				if (!CheckInputs(context, ScriptVerify.P2SH, txdata))
				{
					state.Fail(
							new MemepoolError(
								$"CheckInputs: BUG! PLEASE REPORT THIS! ConnectInputs failed against MANDATORY but not STANDARD flags {context.TransactionHash}"))
						.Throw();
				}
			});

			await this.mempoolScheduler.DoSequential(() =>
			{
				// Remove conflicting transactions from the mempool
				foreach (var it in context.AllConflicting)
				{
					Logging.Logs.Mempool.LogInformation(
						$"replacing tx {it.TransactionHash} with {context.TransactionHash} for {context.ModifiedFees - context.ConflictingFees} BTC additional fees, {context.EntrySize - context.ConflictingSize} delta bytes");
				}
				this.memPool.RemoveStaged(context.AllConflicting, false);

				// This transaction should only count for fee estimation if
				// the node is not behind and it is not dependent on any other
				// transactions in the mempool
				bool validForFeeEstimation = IsCurrentForFeeEstimation() && this.memPool.HasNoInputsOf(tx);

				// Store transaction in memory
				this.memPool.AddUnchecked(context.TransactionHash, context.Entry, context.SetAncestors, validForFeeEstimation);

				// trim mempool and check if tx was trimmed
				if (!fOverrideMempoolLimit)
				{
					LimitMempoolSize(this.nodeArgs.Mempool.MaxMempool*1000000, this.nodeArgs.Mempool.MempoolExpiry*60*60);

					if (!this.memPool.Exists(context.TransactionHash))
						state.Fail(new MemepoolError(MemepoolErrors.REJECT_INSUFFICIENTFEE, "mempool-full")).Throw();
				}
			});

			//	GetMainSignals().SyncTransaction(tx, NULL, CMainSignals::SYNC_TRANSACTION_NOT_IN_BLOCK);

		}

		private void LimitMempoolSize(long limit, long age)
		{
			int expired = this.memPool.Expire(this.dateTimeProvider.GetTime() - age);
			if (expired != 0)
				Logging.Logs.Mempool.LogInformation($"Expired {expired} transactions from the memory pool");

			List<uint256> vNoSpendsRemaining = new List<uint256>();
			this.memPool.TrimToSize(limit, vNoSpendsRemaining);

			//foreach(var removed in vNoSpendsRemaining)
			//	pcoinsTip->Uncache(removed);
		}

		private static bool IsCurrentForFeeEstimation()
		{
			// TODO: implement method

			//AssertLockHeld(cs_main);
			//if (IsInitialBlockDownload())
			//	return false;
			//if (chainActive.Tip()->GetBlockTime() < (GetTime() - MAX_FEE_ESTIMATION_TIP_AGE))
			//	return false;
			//if (chainActive.Height() < pindexBestHeader->nHeight - 1)
			//	return false;
			return true;
		}

		private bool CheckInputs(MempoolValidationContext context, ScriptVerify scriptVerify,
			PrecomputedTransactionData txData)
		{
			var tx = context.Transaction;
			if (!context.Transaction.IsCoinBase)
			{
				this.consensusValidator.CheckIntputs(context.Transaction, context.View.Set, this.chain.Height + 1);

				for (int iInput = 0; iInput < tx.Inputs.Count; iInput++)
				{
					var input = tx.Inputs[iInput];
					int iiIntput = iInput;
					var txout = context.View.GetOutputFor(input);

					if (this.consensusValidator.UseConsensusLib)
					{
						Script.BitcoinConsensusError error;
						return Script.VerifyScriptConsensus(txout.ScriptPubKey, tx, (uint) iiIntput, scriptVerify, out error);
					}
					else
					{
						var checker = new TransactionChecker(tx, iiIntput, txout.Value, txData);
						var ctx = new ScriptEvaluationContext();
						ctx.ScriptVerify = scriptVerify;
						return ctx.VerifyScript(input.ScriptSig, txout.ScriptPubKey, checker);
					}
				}
			}

			return true;
		}

		private bool CheckFinalTx(Transaction tx, Transaction.LockTimeFlags flags)
		{
			// By convention a negative value for flags indicates that the
			// current network-enforced consensus rules should be used. In
			// a future soft-fork scenario that would mean checking which
			// rules would be enforced for the next block and setting the
			// appropriate flags. At the present time no soft-forks are
			// scheduled, so no flags are set.
			flags = (Transaction.LockTimeFlags) Math.Max((int) flags, (int) Transaction.LockTimeFlags.None);

			// CheckFinalTx() uses chainActive.Height()+1 to evaluate
			// nLockTime because when IsFinalTx() is called within
			// CBlock::AcceptBlock(), the height of the block *being*
			// evaluated is what is used. Thus if we want to know if a
			// transaction can be part of the *next* block, we need to call
			// IsFinalTx() with one more than chainActive.Height().
			int nBlockHeight = this.chain.Height + 1;

			// BIP113 will require that time-locked transactions have nLockTime set to
			// less than the median time of the previous block they're contained in.
			// When the next block is created its previous block will be the current
			// chain tip, so we use that to calculate the median time passed to
			// IsFinalTx() if LOCKTIME_MEDIAN_TIME_PAST is set.
			var nBlockTime = flags.HasFlag(ConsensusValidator.StandardLocktimeVerifyFlags)
				? this.chain.Tip.Header.BlockTime
				: DateTimeOffset.FromUnixTimeMilliseconds(this.dateTimeProvider.GetTime());

			return tx.IsFinal(nBlockTime, nBlockHeight);
		}


		// Check if transaction will be BIP 68 final in the next block to be created.
		// Simulates calling SequenceLocks() with data from the tip of the current active chain.
		// Optionally stores in LockPoints the resulting height and time calculated and the hash
		// of the block needed for calculation or skips the calculation and uses the LockPoints
		// passed in for evaluation.
		// The LockPoints should not be considered valid if CheckSequenceLocks returns false.
		// See consensus/consensus.h for flag definitions.
		private bool CheckSequenceLocks(Transaction tx, Transaction.LockTimeFlags flags, LockPoints lp = null,
			bool useExistingLockPoints = false)
		{
			//tx.CheckSequenceLocks()
			// todo:
			return true;
		}


		// Check for standard transaction types
		// @param[in] mapInputs    Map of previous transactions that have outputs we're spending
		// @return True if all inputs (scriptSigs) use only standard transaction forms
		private bool AreInputsStandard(Transaction tx, MemPoolCoinView mapInputs)
		{
			if (tx.IsCoinBase)
				return true; // Coinbases don't use vin normally

			foreach (TxIn txin in tx.Inputs)
			{
				var prev = mapInputs.GetOutputFor(txin);
				var template = StandardScripts.GetTemplateFromScriptPubKey(prev.ScriptPubKey);
				if (template == null)
					return false;

				if (template.Type == TxOutType.TX_SCRIPTHASH)
				{
					if (prev.ScriptPubKey.GetSigOpCount(true) > 15) //MAX_P2SH_SIGOPS
						return false;
				}
			}

			return true;
		}

		private bool IsWitnessStandard(Transaction tx, MemPoolCoinView mapInputs)
		{
			// todo:
			return true;
		}

		public static int GetTransactionWeight(Transaction tx)
		{
			return tx.GetSerializedSize(
				       (ProtocolVersion)
				       ((uint) ProtocolVersion.PROTOCOL_VERSION | MempoolValidator.SERIALIZE_TRANSACTION_NO_WITNESS),
				       SerializationType.Network)*(WITNESS_SCALE_FACTOR - 1) +
			       tx.GetSerializedSize(ProtocolVersion.PROTOCOL_VERSION, SerializationType.Network);
		}

		public static int CalculateModifiedSize(int nTxSize, Transaction trx)
		{
			// In order to avoid disincentivizing cleaning up the UTXO set we don't count
			// the constant overhead for each txin and up to 110 bytes of scriptSig (which
			// is enough to cover a compressed pubkey p2sh redemption) for priority.
			// Providing any more cleanup incentive than making additional inputs free would
			// risk encouraging people to create junk outputs to redeem later.
			if (nTxSize == 0)
				nTxSize = (GetTransactionWeight(trx) + WITNESS_SCALE_FACTOR - 1)/WITNESS_SCALE_FACTOR;

			foreach (var txInput in trx.Inputs)
			{
				var offset = 41U + Math.Min(110U, txInput.ScriptSig.Length);
				if (nTxSize > offset)
					nTxSize -= (int) offset;
			}
			return nTxSize;
		}
	}
}
