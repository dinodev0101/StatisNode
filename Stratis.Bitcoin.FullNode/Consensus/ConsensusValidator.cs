﻿using NBitcoin;
using NBitcoin.Crypto;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static NBitcoin.Transaction;

namespace Stratis.Bitcoin.FullNode.Consensus
{
    public class ConsensusValidator
    {
        NBitcoin.Consensus _ConsensusParams;
        const int MAX_BLOCK_WEIGHT = 4000000;
        private readonly int WITNESS_SCALE_FACTOR = 4;

        public ConsensusValidator(NBitcoin.Consensus consensusParams)
        {
            if(consensusParams == null)
                throw new ArgumentNullException("consensusParams");
            _ConsensusParams = consensusParams;
        }

        public void CheckBlockHeader(BlockHeader header)
        {
            if(!header.CheckProofOfWork())
                ConsensusErrors.HighHash.Throw();
        }

        public void ContextualCheckBlock(Block block, ConsensusFlags consensusFlags, ContextInformation context)
        {
            int nHeight = context.BestBlock == null ? 0 : context.BestBlock.Height + 1;

            // Start enforcing BIP113 (Median Time Past) using versionbits logic.
            var nLockTimeCutoff = consensusFlags.LockTimeFlags.HasFlag(LockTimeFlags.MedianTimePast) ?
                context.BestBlock.MedianTimePast :
                block.Header.BlockTime;

            // Check that all transactions are finalized
            foreach(var transaction in block.Transactions)
            {
                if(!transaction.IsFinal(nLockTimeCutoff, nHeight))
                {
                    ConsensusErrors.BadTransactionNonFinal.Throw();
                }
            }

            // Enforce rule that the coinbase starts with serialized block height
            if(consensusFlags.EnforceBIP34)
            {
                Script expect = new Script(Op.GetPushOp(nHeight));
                Script actual = block.Transactions[0].Inputs[0].ScriptSig;
                if(!StartWith(actual.ToBytes(true), expect.ToBytes(true)))
                {
                    ConsensusErrors.BadCoinbaseHeight.Throw();
                }
            }

            // Validation for witness commitments.
            // * We compute the witness hash (which is the hash including witnesses) of all the block's transactions, except the
            //   coinbase (where 0x0000....0000 is used instead).
            // * The coinbase scriptWitness is a stack of a single 32-byte vector, containing a witness nonce (unconstrained).
            // * We build a merkle tree with all those witness hashes as leaves (similar to the hashMerkleRoot in the block header).
            // * There must be at least one output whose scriptPubKey is a single 36-byte push, the first 4 bytes of which are
            //   {0xaa, 0x21, 0xa9, 0xed}, and the following 32 bytes are SHA256^2(witness root, witness nonce). In case there are
            //   multiple, the last one is used.
            bool fHaveWitness = false;
            if(consensusFlags.ScriptFlags.HasFlag(ScriptVerify.Witness))
            {
                int commitpos = GetWitnessCommitmentIndex(block);
                if(commitpos != -1)
                {
                    bool malleated = false;
                    uint256 hashWitness = BlockWitnessMerkleRoot(block, ref malleated);
                    // The malleation check is ignored; as the transaction tree itself
                    // already does not permit it, it is impossible to trigger in the
                    // witness tree.
                    var witness = block.Transactions[0].Inputs[0].WitScript;
                    if(witness.PushCount != 1 || witness.Pushes.First().Length != 32)
                    {
                        ConsensusErrors.BadWitnessNonceSize.Throw();
                    }

                    byte[] hashed = new byte[64];
                    Buffer.BlockCopy(hashWitness.ToBytes(), 0, hashed, 0, 32);
                    Buffer.BlockCopy(witness.Pushes.First(), 0, hashed, 32, 32);
                    hashWitness = Hashes.Hash256(hashed);
                    if(!EqualsArray(hashWitness.ToBytes(), block.Transactions[0].Outputs[commitpos].ScriptPubKey.ToBytes(true).Skip(6).ToArray(), 32))
                    {
                        ConsensusErrors.BadWitnessMerkleMatch.Throw();
                    }
                    fHaveWitness = true;
                }
            }

            if(!fHaveWitness)
            {
                for(var i = 0; i < block.Transactions.Count; i++)
                {
                    if(block.Transactions[i].HasWitness)
                    {
                        ConsensusErrors.UnexpectedWitness.Throw();
                    }
                }
                
            }

            // After the coinbase witness nonce and commitment are verified,
            // we can check if the block weight passes (before we've checked the
            // coinbase witness, it would be possible for the weight to be too
            // large by filling up the coinbase witness, which doesn't change
            // the block hash, so we couldn't mark the block as permanently
            // failed).
            if(GetBlockWeight(block) > MAX_BLOCK_WEIGHT)
                ConsensusErrors.BadCoinbaseHeight.Throw();
        }

        const int MAX_BLOCK_BASE_SIZE = 1000000;
        public void CheckBlock(Block block)
        {
            bool mutated = false;
            uint256 hashMerkleRoot2 = BlockMerkleRoot(block, ref mutated);
            if(block.Header.HashMerkleRoot != hashMerkleRoot2)
                ConsensusErrors.BadMerkleRoot.Throw();

            // Check for merkle tree malleability (CVE-2012-2459): repeating sequences
            // of transactions in a block without affecting the merkle root of a block,
            // while still invalidating it.
            if(mutated)
                ConsensusErrors.BadTransactionDuplicate.Throw();


            // All potential-corruption validation must be done before we do any
            // transaction validation, as otherwise we may mark the header as invalid
            // because we receive the wrong transactions for it.
            // Note that witness malleability is checked in ContextualCheckBlock, so no
            // checks that use witness data may be performed here.

            // Size limits
            if(block.Transactions.Count == 0 || block.Transactions.Count > MAX_BLOCK_BASE_SIZE || GetSize(block, TransactionOptions.None) > MAX_BLOCK_BASE_SIZE)
                ConsensusErrors.BadBlockLength.Throw();

            // First transaction must be coinbase, the rest must not be
            if(block.Transactions.Count == 0 || !block.Transactions[0].IsCoinBase)
                ConsensusErrors.BadCoinbaseMissing.Throw();
            for(var i = 1; i < block.Transactions.Count; i++)
                if(block.Transactions[i].IsCoinBase)
                    ConsensusErrors.BadMultipleCoinbase.Throw();

            // Check transactions
            foreach(var tx in block.Transactions)
                CheckTransaction(tx);

            var nSigOps = 0;
            foreach(var tx in block.Transactions)
            {
                nSigOps += GetLegacySigOpCount(tx);
            }
            if(nSigOps * WITNESS_SCALE_FACTOR > MAX_BLOCK_SIGOPS_COST)
                ConsensusErrors.BadBlockSigOps.Throw();
        }

        private int GetLegacySigOpCount(Transaction tx)
        {
            var nSigOps = 0;
            foreach(var txin in tx.Inputs)
            {
                nSigOps += (int)txin.ScriptSig.GetSigOpCount(false);
            }
            foreach(var txout in tx.Outputs)
            {
                nSigOps += (int)txout.ScriptPubKey.GetSigOpCount(false);
            }
            return nSigOps;
        }

        public void CheckTransaction(Transaction tx)
        {
            // Basic checks that don't depend on any context
            if(tx.Inputs.Count == 0)
                ConsensusErrors.BadTransactionNoInput.Throw();
            if(tx.Outputs.Count == 0)
                ConsensusErrors.BadTransactionNoOutput.Throw();
            // Size limits (this doesn't take the witness into account, as that hasn't been checked for malleability)
            if(GetSize(tx, TransactionOptions.None) > MAX_BLOCK_BASE_SIZE)
                ConsensusErrors.BadTransactionOversize.Throw();

            // Check for negative or overflow output values
            long nValueOut = 0;
            foreach(var txout in tx.Outputs)
            {
                if(txout.Value.Satoshi < 0)
                    ConsensusErrors.BadTransactionNegativeOutput.Throw();
                if(txout.Value.Satoshi > MAX_MONEY)
                    ConsensusErrors.BadTransactionTooLargeOutput.Throw();
                nValueOut += txout.Value;
                if(!MoneyRange(nValueOut))
                    ConsensusErrors.BadTransactionTooLargeTotalOutput.Throw();
            }

            // Check for duplicate inputs
            HashSet<OutPoint> vInOutPoints = new HashSet<OutPoint>();
            foreach(var txin in tx.Inputs)
            {
                if(vInOutPoints.Contains(txin.PrevOut))
                    ConsensusErrors.BadTransactionDuplicateInputs.Throw();
                vInOutPoints.Add(txin.PrevOut);
            }

            if(tx.IsCoinBase)
            {
                if(tx.Inputs[0].ScriptSig.Length < 2 || tx.Inputs[0].ScriptSig.Length > 100)
                    ConsensusErrors.BadCoinbaseSize.Throw();
            }
            else
            {
                foreach(var txin in tx.Inputs)
                    if(txin.PrevOut.IsNull)
                        ConsensusErrors.BadTransactionNullPrevout.Throw();
            }
        }

        private bool MoneyRange(long nValue)
        {
            return (nValue >= 0 && nValue <= MAX_MONEY);
        }

        /** The maximum allowed number of signature check operations in a block (network rule) */
        const int MAX_BLOCK_SIGOPS_COST = 80000;
        const long MAX_MONEY = 21000000 * Money.COIN;

        private long GetBlockWeight(Block block)
        {
            // This implements the weight = (stripped_size * 4) + witness_size formula,
            // using only serialization with and without witness data. As witness_size
            // is equal to total_size - stripped_size, this formula is identical to:
            // weight = (stripped_size * 3) + total_size.
            return GetSize(block, TransactionOptions.None) * (WITNESS_SCALE_FACTOR - 1) + GetSize(block, TransactionOptions.Witness);
        }

        public int GetSize(IBitcoinSerializable data, TransactionOptions options)
        {
            var bms = new BitcoinStream(Stream.Null, true);
            bms.TransactionOptions = options;
            data.ReadWrite(bms);
            return (int)bms.Counter.WrittenBytes;
        }


        private bool EqualsArray(byte[] a, byte[] b, int len)
        {
            for(int i = 0; i < len; i++)
            {
                if(a[i] != b[i])
                    return false;
            }
            return true;
        }

        private uint256 BlockWitnessMerkleRoot(Block block, ref bool mutated)
        {
            List<uint256> leaves = new List<uint256>();
            leaves.Add(uint256.Zero); // The witness hash of the coinbase is 0.
            foreach(var tx in block.Transactions.Skip(1))
            {
                leaves.Add(tx.GetWitHash());
            }
            return ComputeMerkleRoot(leaves, ref mutated);
        }

        private uint256 BlockMerkleRoot(Block block, ref bool mutated)
        {
            List<uint256> leaves = new List<uint256>(block.Transactions.Count);
            for(int s = 0; s < block.Transactions.Count; s++)
            {
                leaves.Add(block.Transactions[s].GetHash());
            }
            return ComputeMerkleRoot(leaves, ref mutated);
        }

        private uint256 ComputeMerkleRoot(List<uint256> leaves, ref bool mutated)
        {
            uint256 hash = null;
            MerkleComputation(leaves, ref hash, ref mutated, -1, null);
            return hash;
        }

        private void MerkleComputation(List<uint256> leaves, ref uint256 root, ref bool pmutated, int branchpos, List<uint256> pbranch)
        {
            if(pbranch != null)
                pbranch.Clear();
            if(leaves.Count == 0)
            {
                pmutated = false;
                root = uint256.Zero;
                return;
            }
            bool mutated = false;
            // count is the number of leaves processed so far.
            uint count = 0;
            // inner is an array of eagerly computed subtree hashes, indexed by tree
            // level (0 being the leaves).
            // For example, when count is 25 (11001 in binary), inner[4] is the hash of
            // the first 16 leaves, inner[3] of the next 8 leaves, and inner[0] equal to
            // the last leaf. The other inner entries are undefined.
            var inner = new uint256[32];
            for(int i = 0; i < inner.Length; i++)
                inner[i] = uint256.Zero;
            // Which position in inner is a hash that depends on the matching leaf.
            int matchlevel = -1;
            // First process all leaves into 'inner' values.
            while(count < leaves.Count)
            {
                uint256 h = leaves[(int)count];
                bool matchh = count == branchpos;
                count++;
                int level;
                // For each of the lower bits in count that are 0, do 1 step. Each
                // corresponds to an inner value that existed before processing the
                // current leaf, and each needs a hash to combine it.
                for(level = 0; (count & (((UInt32)1) << level)) == 0; level++)
                {
                    if(pbranch != null)
                    {
                        if(matchh)
                        {
                            pbranch.Add(inner[level]);
                        }
                        else if(matchlevel == level)
                        {
                            pbranch.Add(h);
                            matchh = true;
                        }
                    }
                    mutated |= (inner[level] == h);
                    var hash = new byte[64];
                    Buffer.BlockCopy(inner[level].ToBytes(), 0, hash, 0, 32);
                    Buffer.BlockCopy(h.ToBytes(), 0, hash, 32, 32);
                    h = Hashes.Hash256(hash);
                }
                // Store the resulting hash at inner position level.
                inner[level] = h;
                if(matchh)
                {
                    matchlevel = level;
                }
            }
            // Do a final 'sweep' over the rightmost branch of the tree to process
            // odd levels, and reduce everything to a single top value.
            // Level is the level (counted from the bottom) up to which we've sweeped.
            int levell = 0;
            // As long as bit number level in count is zero, skip it. It means there
            // is nothing left at this level.
            while((count & (((UInt32)1) << levell)) == 0)
            {
                levell++;
            }
            uint256 hh = inner[levell];
            bool matchhh = matchlevel == levell;
            while(count != (((UInt32)1) << levell))
            {
                // If we reach this point, h is an inner value that is not the top.
                // We combine it with itself (Bitcoin's special rule for odd levels in
                // the tree) to produce a higher level one.
                if(pbranch != null && matchhh)
                {
                    pbranch.Add(hh);
                }

                var hash = new byte[64];
                Buffer.BlockCopy(hh.ToBytes(), 0, hash, 0, 32);
                Buffer.BlockCopy(hh.ToBytes(), 0, hash, 32, 32);
                hh = Hashes.Hash256(hash);
                // Increment count to the value it would have if two entries at this
                // level had existed.
                count += (((uint)1) << levell);
                levell++;
                // And propagate the result upwards accordingly.
                while((count & (((uint)1) << levell)) == 0)
                {
                    if(pbranch != null)
                    {
                        if(matchhh)
                        {
                            pbranch.Add(inner[levell]);
                        }
                        else if(matchlevel == levell)
                        {
                            pbranch.Add(hh);
                            matchhh = true;
                        }
                    }

                    var hashh = new byte[64];
                    Buffer.BlockCopy(inner[levell].ToBytes(), 0, hashh, 0, 32);
                    Buffer.BlockCopy(hh.ToBytes(), 0, hashh, 32, 32);
                    hh = Hashes.Hash256(hashh);

                    levell++;
                }
            }
            // Return result.			
            pmutated = mutated;
            root = hh;
        }

        private int GetWitnessCommitmentIndex(Block block)
        {
            int commitpos = -1;
            for(var o = 0; o < block.Transactions[0].Outputs.Count; o++)
            {
                if(block.Transactions[0].Outputs[o].ScriptPubKey.Length >= 38 &&
                    block.Transactions[0].Outputs[o].ScriptPubKey.ToBytes(true)[0] == (byte)OpcodeType.OP_RETURN &&
                    block.Transactions[0].Outputs[o].ScriptPubKey.ToBytes(true)[1] == 0x24 &&
                    block.Transactions[0].Outputs[o].ScriptPubKey.ToBytes(true)[2] == 0xaa &&
                    block.Transactions[0].Outputs[o].ScriptPubKey.ToBytes(true)[3] == 0x21 &&
                    block.Transactions[0].Outputs[o].ScriptPubKey.ToBytes(true)[4] == 0xa9 &&
                    block.Transactions[0].Outputs[o].ScriptPubKey.ToBytes(true)[5] == 0xed)
                {
                    commitpos = o;
                }
            }
            return commitpos;
        }

        private bool StartWith(byte[] bytes, byte[] subset)
        {
            if(bytes.Length < subset.Length)
                return false;
            for(int i = 0; i < subset.Length; i++)
            {
                if(subset[i] != bytes[i])
                    return false;
            }
            return true;
        }

        public void ContextualCheckBlockHeader(BlockHeader header, ContextInformation context)
        {
            if(context.BestBlock == null)
                throw new ArgumentException("context.BestBlock should not be null");
            int nHeight = context.BestBlock.Height + 1;

            // Check proof of work
            if(header.Bits != context.NextWorkRequired)
                ConsensusErrors.BadDiffBits.Throw();

            // Check timestamp against prev
            if(header.BlockTime <= context.BestBlock.MedianTimePast)
                ConsensusErrors.TimeTooOld.Throw();

            // Check timestamp
            if(header.BlockTime > context.Time + TimeSpan.FromHours(2))
                ConsensusErrors.TimeTooNew.Throw();

            // Reject outdated version blocks when 95% (75% on testnet) of the network has upgraded:
            // check for version 2, 3 and 4 upgrades
            if((header.Version < 2 && nHeight >= _ConsensusParams.BuriedDeployments[BuriedDeployments.BIP34]) ||
               (header.Version < 3 && nHeight >= _ConsensusParams.BuriedDeployments[BuriedDeployments.BIP66]) ||
               (header.Version < 4 && nHeight >= _ConsensusParams.BuriedDeployments[BuriedDeployments.BIP65]))
                ConsensusErrors.BadVersion.Throw();
        }
    }
}
