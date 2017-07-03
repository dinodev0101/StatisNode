﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Utilities;
using DBreeze.Utils;

namespace Stratis.Bitcoin.BlockStore
{
	public interface IBlockRepository : IDisposable
	{
		Task Initialize();

		Task PutAsync(uint256 nextBlockHash, List<Block> blocks);

		Task<Block> GetAsync(uint256 hash);

		Task<Transaction> GetTrxAsync(uint256 trxid);

        Task<List<byte[]>> GetTrxOutByAddrAsync(BitcoinAddress addr);

        Task<List<uint256>> GetTrxOutNextAsync(List<byte[]> trxOutList);

		Task DeleteAsync(uint256 newlockHash, List<uint256> hashes);

		Task<bool> ExistAsync(uint256 hash);

		Task<uint256> GetTrxBlockIdAsync(uint256 trxid);

		Task SetBlockHash(uint256 nextBlockHash);

		Task SetTxIndex(bool txIndex);
	}

	public class BlockRepository : IBlockRepository
	{
		private readonly DBreezeSingleThreadSession session;
		private readonly Network network;
		private static readonly byte[] BlockHashKey = new byte[0];
		private static readonly byte[] TxIndexKey = new byte[1];
		public BlockStoreRepositoryPerformanceCounter PerformanceCounter { get; }

        public class TransactionOutputID
        {
            public const int Length = 36;
            public byte[] Value { get; }

            public TransactionOutputID(uint256 transactionHash, uint txOutN)
            {
                this.Value = transactionHash.ToBytes().Concat(txOutN.ToBytes());
            }
        }

		public BlockRepository(Network network, DataFolder dataFolder)
			: this(network, dataFolder.BlockPath)
		{
		}

		public BlockRepository(Network network, string folder)
		{
			Guard.NotNull(network, nameof(network));
			Guard.NotEmpty(folder, nameof(folder));

			this.session = new DBreezeSingleThreadSession("DBreeze BlockRepository", folder);
			this.network = network;
			this.PerformanceCounter = new BlockStoreRepositoryPerformanceCounter();
		}

		public Task Initialize()
		{
			var genesis = this.network.GetGenesis();

			var sync = this.session.Do(() =>
			{
				this.session.Transaction.SynchronizeTables("Block", "Transaction", "Script", "Output", "Common");
				this.session.Transaction.ValuesLazyLoadingIsOn = true;
			});

			var hash = this.session.Do(() =>
			{
				if (this.LoadBlockHash() == null)
				{
					this.SaveBlockHash(genesis.GetHash());
					this.session.Transaction.Commit();
				}
				if (this.LoadTxIndex() == null)
				{
					this.SaveTxIndex(false);
					this.session.Transaction.Commit();
				}
			});

			return Task.WhenAll(new[] { sync, hash });
		}

		public bool LazyLoadingOn
		{
			get { return this.session.Transaction.ValuesLazyLoadingIsOn; }
			set { this.session.Transaction.ValuesLazyLoadingIsOn = value; }
		}

        public Task<Transaction> GetTrxAsync(uint256 trxid)
		{
			Guard.NotNull(trxid, nameof(trxid));

			if (!this.TxIndex)
				return Task.FromResult(default(Transaction));

			return this.session.Do(() =>
			{
				var blockid = this.session.Transaction.Select<byte[], uint256>("Transaction", trxid.ToBytes());
                if (!blockid.Exists)
                {
                    this.PerformanceCounter.AddRepositoryMissCount(1);
                    return null;
                }

                this.PerformanceCounter.AddRepositoryHitCount(1);
                var block = this.session.Transaction.Select<byte[], Block>("Block", blockid.Value.ToBytes());
				var trx = block?.Value?.Transactions.FirstOrDefault(t => t.GetHash() == trxid);

                if (trx == null)
                {
                    this.PerformanceCounter.AddRepositoryMissCount(1);
                }
                else
                {
                    this.PerformanceCounter.AddRepositoryHitCount(1);
                }

                return trx;
            });
		}

        public Task<List<byte[]>> GetTrxOutByAddrAsync(BitcoinAddress addr)
        {
            Guard.NotNull(addr, nameof(addr));

            if (!this.TxIndex)
                return Task.FromResult(default(List<byte[]>));

            return this.session.Do(() =>
            {
                return this.GetAddressTransactionOutputs(addr).ToList();
            });
        }

        public Task<List<uint256>> GetTrxOutNextAsync(List<byte[]> trxOutList)
        {
            Guard.NotNull(trxOutList, nameof(trxOutList));

            if (!this.TxIndex)
                return Task.FromResult(default(List<uint256>));

            return this.session.Do(() =>
            {
                var trxList = new List<uint256>();

                foreach (var trxOut in trxOutList)
                {
                    if (trxOut != null)
                    {
                        Guard.Assert(trxOut.Length == TransactionOutputID.Length);

                        var trxId = this.session.Transaction.Select<byte[], uint256>("Output", trxOut);

                        if (trxId.Exists)
                        {
                            this.PerformanceCounter.AddRepositoryHitCount(1);
                            trxList.Add(trxId.Value);
                            continue;
                        }
                    }

                    this.PerformanceCounter.AddRepositoryMissCount(1);
                    trxList.Add(null);
                }

                return trxList;
            });
        }

        public Task<uint256> GetTrxBlockIdAsync(uint256 trxid)
		{
			Guard.NotNull(trxid, nameof(trxid));

			if (!this.TxIndex)
				return Task.FromResult(default(uint256));

			return this.session.Do(() =>
			{
				var blockid = this.session.Transaction.Select<byte[], uint256>("Transaction", trxid.ToBytes());

                if (!blockid.Exists)
                {
                    this.PerformanceCounter.AddRepositoryMissCount(1);
                    return null;
                }
                else
                {
                    this.PerformanceCounter.AddRepositoryHitCount(1);
                    return blockid.Value;
                }
			});
		}		

		public uint256 BlockHash { get; private set; }
		public bool TxIndex { get; private set; }

		public Task PutAsync(uint256 nextBlockHash, List<Block> blocks)
		{
			Guard.NotNull(nextBlockHash, nameof(nextBlockHash));
			Guard.NotNull(blocks, nameof(blocks));

            // dbreeze is faster if sort ascending by key in memory before insert
            // however we need to find how byte arrays are sorted in dbreeze this link can help 
            // https://docs.google.com/document/pub?id=1IFkXoX3Tc2zHNAQN9EmGSXZGbabMrWmpmVxFsLxLsw

            // Use this comparer. We are assuming that DBreeze would use the same comparer for
            // ordering rows in the file.
            var byteListComparer = new ByteListComparer();

            return this.session.Do(() =>
			{
                var blockDict = new Dictionary<uint256, Block>();
                var transDict = new Dictionary<uint256, uint256>();
                var addrDict = new Dictionary<byte[], HashSet<byte[]>>();
                var outputDict = new Dictionary<byte[], uint256>();

                // Gather blocks
                foreach (var block in blocks)
                {
                    var blockId = block.GetHash();
                    blockDict[blockId] = block;
                    // Gaher transactions
                    if (this.TxIndex)
                    {
                        foreach (var transaction in block.Transactions)
                        {
                            var trxId = transaction.GetHash();
                            transDict[trxId] = blockId;
                            // Gather addresses
                            foreach (var (script, trxOutN) in this.IndexedTransactionOutputs(transaction))
                            {
                                var value = new TransactionOutputID(trxId, trxOutN).Value;

                                HashSet<byte[]> list = addrDict.TryGet(script);
                                if (list == null)
                                {
                                    list = new HashSet<byte[]>();
                                    addrDict[script] = list;
                                }
                                list.Add(value);
                            }

                            foreach (var input in transaction.Inputs)
                            {
                                var key = new TransactionOutputID(input.PrevOut.Hash, input.PrevOut.N).Value;
                                var value = transaction.GetHash();
                                outputDict[key] = value;
                            }
                        }
                    }
                }

                // Sort blocks. Be consistent in always converting our keys to byte arrays using the ToBytes method.
                var blockList = blockDict.ToList();      
                blockList.Sort((pair1, pair2) => byteListComparer.Compare(pair1.Key.ToBytes(), pair2.Key.ToBytes()));

                // Index blocks
                foreach (KeyValuePair<uint256, Block> kv in blockList)
                {
                    var blockId = kv.Key;
                    var block = kv.Value;

                    // if the block is already in store don't write it again
                    var item = this.session.Transaction.Select<byte[], Block>("Block", blockId.ToBytes());
                    if (!item.Exists)
                    {
                        this.PerformanceCounter.AddRepositoryMissCount(1);
                        this.PerformanceCounter.AddRepositoryInsertCount(1);
                        this.session.Transaction.Insert<byte[], Block>("Block", blockId.ToBytes(), block);
                    }
                    else
                    {
                        this.PerformanceCounter.AddRepositoryHitCount(1);
                    }
                }

                // Sort transactions. Be consistent in always converting our keys to byte arrays using the ToBytes method.
                var transList = transDict.ToList();
                transList.Sort((pair1, pair2) => byteListComparer.Compare(pair1.Key.ToBytes(), pair2.Key.ToBytes()));

                // Index transactions
                foreach (KeyValuePair<uint256, uint256> kv in transList)
                {
                    var trxId = kv.Key;
                    var blockId = kv.Value;

                    this.PerformanceCounter.AddRepositoryInsertCount(1);
                    this.session.Transaction.Insert<byte[], uint256>("Transaction", trxId.ToBytes(), blockId);
                }

                // Sort addresses. Be consistent in always converting our keys to byte arrays using the ToBytes method.
                var addrList = addrDict.ToList();
                addrList.Sort((pair1, pair2) => byteListComparer.Compare(pair1.Key.ToBytes(), pair2.Key.ToBytes()));

                // Index addresses
                foreach (KeyValuePair<byte[], HashSet<byte[]>> kv in addrList)
                {
                    var script = kv.Key;
                    var transactions = kv.Value;

                    this.PerformanceCounter.AddRepositoryInsertCount(1);
                    this.RecordTransactionOutputsToScript(script, transactions);
                }

                // Sort outputs. In this case the keys are already arrays of bytes
                var outputList = outputDict.ToList();
                outputList.Sort((pair1, pair2) => byteListComparer.Compare(pair1.Key, pair2.Key));

                // Index blocks
                foreach (KeyValuePair<byte[], uint256> kv in outputList)
                {
                    var trxOutputN = kv.Key;
                    var trxNext = kv.Value.ToBytes();

                    // if the block is already in store don't write it again
                    var item = this.session.Transaction.Select<byte[], byte[]>("Output", trxOutputN);
                    if (!item.Exists)
                    {
                        this.PerformanceCounter.AddRepositoryMissCount(1);
                        this.PerformanceCounter.AddRepositoryInsertCount(1);
                        this.session.Transaction.Insert<byte[], byte[]>("Output", trxOutputN, trxNext);
                    }
                    else
                    {
                        this.PerformanceCounter.AddRepositoryHitCount(1);
                    }
                }

                // Commit additions
                this.SaveBlockHash(nextBlockHash);
				this.session.Transaction.Commit();
			});
		}

        private IEnumerable<(byte[], uint)> IndexedTransactionOutputs(Transaction transaction)
        {
            for (uint N = 0; N < transaction.Outputs.Count; N++)
            {
                var output = transaction.Outputs[N];

                var script = output.ScriptPubKey.ToBytes(true);

                // P2PKH - "Pay to this bitcoin address". 
                // TODO: Selecting which scripts to index should be configurable or this condition should be removed
                if (script.Length == 25 && script[0] == (byte)OpcodeType.OP_DUP && script[1] == (byte)OpcodeType.OP_HASH160 && script[2] == 20 &&
                    script[23] == (byte)OpcodeType.OP_EQUALVERIFY && script[24] == (byte)OpcodeType.OP_CHECKSIG)
                    yield return (script, N);
            }
        }

        private bool? LoadTxIndex()
		{
			var item = this.session.Transaction.Select<byte[], bool>("Common", TxIndexKey);

            if (!item.Exists)
            {
                this.PerformanceCounter.AddRepositoryMissCount(1);
                return null;
            }
            else
            {
                this.PerformanceCounter.AddRepositoryHitCount(1);
                this.TxIndex = item.Value;
                return item.Value;
            }
		}
		private void SaveTxIndex(bool txIndex)
		{
			this.TxIndex = txIndex;
            this.PerformanceCounter.AddRepositoryInsertCount(1);
			this.session.Transaction.Insert<byte[], bool>("Common", TxIndexKey, txIndex);
		}

		public Task SetTxIndex(bool txIndex)
		{
			return this.session.Do(() =>
			{
				this.SaveTxIndex(txIndex);
				this.session.Transaction.Commit();
			});
		}

		private uint256 LoadBlockHash()
		{
			this.BlockHash = this.BlockHash ?? this.session.Transaction.Select<byte[], uint256>("Common", BlockHashKey)?.Value;
			return this.BlockHash;
		}

		public Task SetBlockHash(uint256 nextBlockHash)
		{
			Guard.NotNull(nextBlockHash, nameof(nextBlockHash));

			return this.session.Do(() =>
			{
				this.SaveBlockHash(nextBlockHash);
				this.session.Transaction.Commit();
			});
		}

		private void SaveBlockHash(uint256 nextBlockHash)
		{
			this.BlockHash = nextBlockHash;
            this.PerformanceCounter.AddRepositoryInsertCount(1);
			this.session.Transaction.Insert<byte[], uint256>("Common", BlockHashKey, nextBlockHash);
		}

		public Task<Block> GetAsync(uint256 hash)
		{
			Guard.NotNull(hash, nameof(hash));

			return this.session.Do(() =>
			{
				var key = hash.ToBytes();                
                var item = this.session.Transaction.Select<byte[], Block>("Block", key);
                if (!item.Exists)
                {
                    this.PerformanceCounter.AddRepositoryMissCount(1);
                }
                else
                {
                    this.PerformanceCounter.AddRepositoryHitCount(1);
                }

                return item?.Value;                
			});
		}

		public Task<bool> ExistAsync(uint256 hash)
		{
			Guard.NotNull(hash, nameof(hash));

			return this.session.Do(() =>
			{
				var key = hash.ToBytes();
				var item = this.session.Transaction.Select<byte[], Block>("Block", key);
                if (!item.Exists)
                {
                    this.PerformanceCounter.AddRepositoryMissCount(1);                    
                }
                else
                {
                    this.PerformanceCounter.AddRepositoryHitCount(1);                
                }

                return item.Exists; // lazy loading is on so we don't fetch the whole value, just the row.
            });
		}

		public Task DeleteAsync(uint256 newlockHash, List<uint256> hashes)
		{
			Guard.NotNull(newlockHash, nameof(newlockHash));
			Guard.NotNull(hashes, nameof(hashes));

            return this.session.Do(() =>
            {
                foreach (var hash in hashes)
                {
                    // if the block is already in store don't write it again
                    var key = hash.ToBytes();

                    if (this.TxIndex)
                    {
                        var block = this.session.Transaction.Select<byte[], Block>("Block", key);
                        if (block.Exists)
                        {
                            this.PerformanceCounter.AddRepositoryHitCount(1);

                            var removals = new Dictionary<byte[], HashSet<byte[]>>();

                            foreach (var transaction in block.Value.Transactions)
                            {
                                var trxId = transaction.GetHash();

                                this.PerformanceCounter.AddRepositoryDeleteCount(1);
                                this.session.Transaction.RemoveKey<byte[]>("Transaction", trxId.ToBytes());

                                // Remove transaction reference from indexed addresses
                                foreach (var (script, trxOutN) in this.IndexedTransactionOutputs(transaction))
                                {
                                    var value = new TransactionOutputID(trxId, trxOutN).Value;

                                    HashSet<byte[]> list;
                                    if (!removals.TryGetValue(script, out list))
                                    {
                                        list = new HashSet<byte[]>();
                                        removals[script] = list;
                                    }

                                    list.Add(value);
                                }

                                foreach (var input in transaction.Inputs)
                                {
                                    var key2 = new TransactionOutputID(input.PrevOut.Hash, input.PrevOut.N).Value;
                                    this.PerformanceCounter.AddRepositoryDeleteCount(1);
                                    this.session.Transaction.RemoveKey<byte[]>("Output", key2);
                                }
                            }

                            foreach (KeyValuePair<byte[], HashSet<byte[]>> kv in removals)
                                this.PerformanceCounter.AddRepositoryDeleteCount(
                                    this.RemoveTransactionOutputsToScript(kv.Key, kv.Value));
                        }
                        else
                        {
                            this.PerformanceCounter.AddRepositoryMissCount(1);
                        }
			        }

                    this.PerformanceCounter.AddRepositoryDeleteCount(1);
                    this.session.Transaction.RemoveKey<byte[]>("Block", key);
			    }

			    this.SaveBlockHash(newlockHash);
			    this.session.Transaction.Commit();
			});
		}

        private IEnumerable<byte[]> GetAddressTransactionOutputs(BitcoinAddress addr)
        {
            var addrBytes = addr.ToBytes();

            Guard.Assert(addrBytes.Length == 20);

            var script = new byte[25];
            script[0] = (byte)OpcodeType.OP_DUP;
            script[1] = (byte)OpcodeType.OP_HASH160;
            script[2] = 20;
            script.CopyInside(3, addrBytes);
            script[23] = (byte)OpcodeType.OP_EQUALVERIFY;
            script[24] = (byte)OpcodeType.OP_CHECKSIG;

            foreach (var output in this.GetScriptTransactionOutputs(script))
                yield return output;
        }

        private IEnumerable<byte[]> GetScriptTransactionOutputs(byte[] script)
        {
            var addrRow = this.session.Transaction.Select<byte[], byte[]>("Script", script);
            if (addrRow.Exists)
            {
                // Get the number of transactions
                var cntBytes = addrRow.GetValuePart(0, sizeof(uint));
                if (!BitConverter.IsLittleEndian) Array.Reverse(cntBytes);
                uint cnt = BitConverter.ToUInt32(cntBytes, 0);

                // Any transactions recorded?
                if (cnt > 0)
                {
                    byte[] listBytes = addrRow.GetValuePart(sizeof(uint), (uint)(cnt * TransactionOutputID.Length));
                    for (int i = 0; i < cnt; i++)
                    {
                        var trxOutN = new byte[TransactionOutputID.Length];
                        Array.Copy(listBytes, i * TransactionOutputID.Length, trxOutN, 0, TransactionOutputID.Length);
                        yield return trxOutN;
                    }
                }
            }
        }

        private void RecordTransactionOutputsToScript(byte[] script, HashSet<byte[]> list)
        {
            uint cnt = 0;
            var addrRow = this.session.Transaction.Select<byte[], byte[]>("Script", script);

            if (addrRow.Exists)
            {
                // Get the number of transaction hashes
                var bytes = addrRow.GetValuePart(0, sizeof(uint));
                if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
                cnt = BitConverter.ToUInt32(bytes, 0);
            }

            // Force the value's size to a power of 2 that will fit all the additional transaction hashes
            if ((cnt + list.Count) >= 2 && (addrRow.Value == null || 
                (sizeof(uint) + (cnt + list.Count) * TransactionOutputID.Length) > addrRow.Value.Length))
            {
                uint growCnt = 2;
                for (; growCnt < (cnt + list.Count); growCnt *= 2) { }
                uint growSize = (uint)(sizeof(uint) + growCnt * TransactionOutputID.Length); 
                this.session.Transaction.InsertPart<byte[], byte[]>("Script", script, new byte[] { 0 /* Dummy */}, growSize - 1);
            }

            // Add the transaction hashes
            foreach (var trxId in list)
            {
                Guard.Assert(trxId.Length == TransactionOutputID.Length);
                this.session.Transaction.InsertPart<byte[], byte[]>("Script", script, trxId,
                    (uint)(sizeof(uint) + (cnt++) * TransactionOutputID.Length));
            }

            // Update the number of transaction hashes
            var cntBytes = BitConverter.GetBytes(cnt);
            if (!BitConverter.IsLittleEndian) Array.Reverse(cntBytes);
            
            this.session.Transaction.InsertPart<byte[], byte[]>("Script", script, cntBytes, 0);
        }

        private int RemoveTransactionOutputsToScript(byte[] script, HashSet<byte[]> outputs)
        {
            var addrRow = this.session.Transaction.Select<byte[], byte[]>("Script", script);
            if (!addrRow.Exists)
                return 0;

            // Storing one-to-many value as count followed by count transaction hashes
            
            // Get the number of transactions
            var cntBytes = addrRow.GetValuePart(0, sizeof(uint));
            if (!BitConverter.IsLittleEndian) Array.Reverse(cntBytes);
            uint cnt = BitConverter.ToUInt32(cntBytes, 0);

            int removeCnt = 0;

            // Any transactions recorded?
            foreach (var output in outputs)
            {
                Guard.Assert(output.Length == TransactionOutputID.Length);

                if (cnt > 0)
                {
                    // Get all the transactions hashes for easy iteration
                    byte[] listBytes = addrRow.GetValuePart(sizeof(uint), (uint)(cnt * TransactionOutputID.Length));

                    // Traverse in reverse order
                    bool found = false;

                    int i = listBytes.Length - TransactionOutputID.Length;
                    for (; i >= 0; i -= TransactionOutputID.Length)
                    {
                        int j = 0;
                        // trxid found?
                        for (; j < TransactionOutputID.Length; j++)
                            if (listBytes[i + j] != output[j])
                                break;
                        found = (j == TransactionOutputID.Length);
                        if (found) break;
                    }

                    // Transaction was not found
                    if (!found) continue;

                    // Found the trxid. Now remove it by overwriting the rest of the list with a shifted version
                    var replaceBytes = new byte[listBytes.Length - i];
                    Array.Copy(listBytes, i + TransactionOutputID.Length, replaceBytes, 0, replaceBytes.Length - TransactionOutputID.Length);

                    // Overwrite the value in the table row
                    this.session.Transaction.InsertPart<byte[], byte[]>("Script", script, replaceBytes, (uint)(i + sizeof(uint)));

                    // Update the number of items
                    if (--cnt > 0)
                    {
                        var cntBytes2 = BitConverter.GetBytes(cnt);
                        if (!BitConverter.IsLittleEndian) Array.Reverse(cntBytes2);
                        this.session.Transaction.InsertPart<byte[], byte[]>("Script", script, cntBytes2, 0);
                        continue;
                    }

                    removeCnt++;
                }
            }

            // Key is no longer needed
            if (cnt == 0)
                this.session.Transaction.RemoveKey<byte[]>("Script", script);

            return removeCnt;
        }

		public void Dispose()
		{
			this.session.Dispose();
		}
	}
}
