﻿using System.Collections.Generic;
using NBitcoin;
using SQLite;
using Stratis.Features.SQLiteWalletRepository.Tables;

namespace Stratis.Features.SQLiteWalletRepository
{
    /// <summary>
    /// This class represents a connection to the repository. Its a central point for all functionality that can be performed via a connection.
    /// </summary>
    public class DBConnection : SQLiteConnection
    {
        public SQLiteWalletRepository repo;

        public DBConnection(SQLiteWalletRepository repo) : base(repo.DBFile)
        {
            this.repo = repo;
        }

        internal List<HDAddress> CreateAddresses(HDAccount account, int addressType, int addressesQuantity)
        {
            var addresses = new List<HDAddress>();

            int addressCount = HDAddress.GetAddressCount(this, account.WalletId, account.AccountIndex, addressType);

            for (int addressIndex = addressCount; addressIndex < (addressCount + addressesQuantity); addressIndex++)
                addresses.Add(CreateAddress(account, addressType, addressIndex));

            return addresses;
        }

        internal void TopUpAddresses(int walletId, int accountIndex, int addressType)
        {
            int addressCount = HDAddress.GetAddressCount(this, walletId, accountIndex, addressType);
            int nextAddressIndex = HDAddress.GetNextAddressIndex(this, walletId, accountIndex, addressType);
            int buffer = addressCount - nextAddressIndex;

            var account = HDAccount.GetAccount(this, walletId, accountIndex);

            for (int addressIndex = addressCount; buffer < 20; buffer++, addressIndex++)
                CreateAddress(account, addressType, addressIndex);
        }

        internal HDAddress CreateAddress(HDAccount account, int addressType, int addressIndex)
        {
            // Retrieve the pubkey associated with the private key of this address index.
            var keyPath = new KeyPath($"{addressType}/{addressIndex}");

            ExtPubKey extPubKey = ExtPubKey.Parse(account.ExtPubKey, this.repo.Network).Derive(keyPath);
            PubKey pubKey = extPubKey.PubKey;
            Script pubKeyScript = pubKey.ScriptPubKey;
            Script scriptPubKey = this.repo.ScriptPubKeyProvider.FromPubKey(pubKey, account.ScriptPubKeyType);

            // Add the new address details to the list of addresses.
            return this.CreateAddress(account, addressType, addressIndex, pubKeyScript.ToHex(), scriptPubKey.ToHex());
        }

        internal HDAddress CreateAddress(HDAccount account, int addressType, int addressIndex, string pubKey, string scriptPubKey)
        {
            // Add the new address details to the list of addresses.
            var newAddress = new HDAddress
            {
                WalletId = account.WalletId,
                AccountIndex = account.AccountIndex,
                AddressType = addressType,
                AddressIndex = addressIndex,
                PubKey = pubKey,
                ScriptPubKey = scriptPubKey,
                TransactionCount = 0
            };

            this.Insert(newAddress);

            return newAddress;
        }

        internal IEnumerable<HDAddress> GetUsedAddresses(int walletId, int accountIndex, int addressType)
        {
            return HDAddress.GetUsedAddresses(this, walletId, accountIndex, addressType);
        }

        internal HDAccount CreateAccount(int walletId, int accountIndex, string accountName, string extPubKey, string scriptPubKeyType, int creationTimeSeconds)
        {
            var account = new HDAccount()
            {
                WalletId = walletId,
                AccountIndex = accountIndex,
                AccountName = accountName,
                ExtPubKey = extPubKey,
                CreationTime = creationTimeSeconds,
                ScriptPubKeyType = scriptPubKeyType
            };

            this.Insert(account);

            return account;
        }

        internal void CreateTable<T>()
        {
            if (typeof(T) == typeof(HDWallet))
                HDWallet.CreateTable(this);
            else if (typeof(T) == typeof(HDAccount))
                HDAccount.CreateTable(this);
            else if (typeof(T) == typeof(HDAddress))
                HDAddress.CreateTable(this);
            else if (typeof(T) == typeof(HDTransactionData))
                HDTransactionData.CreateTable(this);
        }

        internal HDWallet GetWalletByName(string walletName)
        {
            return HDWallet.GetByName(this, walletName);
        }

        internal HDAccount GetAccountByName(string walletName, string accountName)
        {
            return this.FindWithQuery<HDAccount>($@"
                SELECT  A.*
                FROM    HDAccount A
                JOIN    HDWallet W
                ON      W.Name = ?
                AND     W.WalletId = A.WalletId
                WHERE   A.AccountName = ?", walletName, accountName);
        }

        internal IEnumerable<HDAccount> GetAccounts(int walletId)
        {
            return HDAccount.GetAccounts(this, walletId);
        }

        internal HDWallet GetById(int walletId)
        {
            return this.Find<HDWallet>(walletId);
        }

        internal HDAccount GetById(int walletId, int accountIndex)
        {
            return HDAccount.GetAccount(this, walletId, accountIndex);
        }

        internal IEnumerable<HDAddress> GetUnusedAddresses(int walletId, int accountIndex, int addressType, int count)
        {
            return HDAddress.GetUnusedAddresses(this, walletId, accountIndex, addressType, count);
        }

        internal IEnumerable<HDTransactionData> GetSpendableOutputs(int walletId, int accountIndex, int currentChainHeight, long coinbaseMaturity, int confirmations = 0)
        {
            return HDTransactionData.GetSpendableTransactions(this, walletId, accountIndex, currentChainHeight, coinbaseMaturity, confirmations);
        }

        internal IEnumerable<HDTransactionData> GetTransactionsForAddress(int walletId, int accountIndex, int addressType, int addressIndex)
        {
            return HDTransactionData.GetAllTransactions(this, walletId, accountIndex, addressType, addressIndex);
        }

        internal void RemoveTransactionsAfterLastBlockSynced(int lastBlockSyncedHeight, int? walletId = null)
        {
            string outputFilter = (walletId == null) ? $@"
            WHERE   OutputBlockHeight > {lastBlockSyncedHeight}" : $@"
            WHERE   WalletId = {walletId}
            AND     OutputBlockHeight > {lastBlockSyncedHeight}";

            string spendFilter = (walletId == null) ? $@"
            WHERE   SpendBlockHeight > {lastBlockSyncedHeight}" : $@"
            WHERE   WalletId = {walletId}
            AND     SpendBlockHeight > {lastBlockSyncedHeight}";

            this.Execute($@"
            CREATE  TABLE temp.TxToDelete (
                    WalletId INT
            ,       AccountIndex INT
            ,       AddressType INT
            ,       AddressIndex INT
            ,       TransactionDataIndex INT)");

            this.Execute($@"
            INSERT  INTO temp.TxToDelete (
                    WalletId
            ,       AccountIndex
            ,       AddressType
            ,       AddressIndex
            ,       TransactionDataIndex)
            SELECT  WalletId
            ,       AccountIndex
            ,       AddressType
            ,       AddressIndex
            ,       TransactionDataIndex
            FROM    HDTransactionData
            {outputFilter}");

            this.Execute($@"
            CREATE  TABLE temp.TxCountsToAdjust (
                    WalletId INT
            ,       AccountIndex INT
            ,       AddressType INT
            ,       AddressIndex INT
            ,       TransactionCount INT)");

            this.Execute($@"
            INSERT  INTO temp.TxCountsToAdjust
            SELECT  T.WalletId
            ,       T.AccountIndex
            ,       T.AddressType
            ,       T.AddressIndex
            ,       COUNT(*)
            FROM    temp.TxToDelete T
            GROUP   BY WalletId, AccountIndex, AddressType, AddressIndex");

            this.Execute($@"
            DELETE  FROM HDTransactionData
            WHERE   (WalletId, AccountIndex, AddressType, AddressIndex, TransactionDataIndex) IN (
                    SELECT  WalletId, AccountIndex, AddressType, AddressIndex, TransactionDataIndex
                    FROM    temp.TxToDelete)");

            this.Execute($@"
            UPDATE  HDAddress
            SET     TransactionCount = TransactionCount - (
                    SELECT  T.TransactionCount
                    FROM    temp.TxCountsToAdjust T
                    JOIN    HDAddress A
                    ON      A.WalletId = T.WalletId
                    AND     A.AccountIndex = T.AccountIndex
                    AND     A.AddressType = T.AddressType
                    AND     A.AddressIndex = T.AddressIndex)
            WHERE   (WalletId, AccountIndex, AddressType, AddressIndex) IN (
                    SELECT  WalletId, AccountIndex, AddressType, AddressIndex
                    FROM    temp.TxCountsToAdjust)");

            this.Execute($@"
            UPDATE  HDTransactionData
            SET     SpendBlockHeight = NULL
            ,       SpendBlockHash = NULL
            ,       SpendTxTime = NULL
            ,       SpendTxId = NULL
            ,       SpendTxRecipient = NULL
            ,       SpendTxTotalOut = NULL
            {spendFilter}");
        }

        /// <summary>
        /// Only keep wallet transactions up to and including the specified block.
        /// </summary>
        /// <param name="walletName">The name of the wallet.</param>
        /// <param name="lastBlockSynced">The last block synced to set.</param>
        internal void SetLastBlockSynced(string walletName, ChainedHeader lastBlockSynced)
        {
            var wallet = this.GetWalletByName(walletName);
            wallet.SetLastBlockSynced(lastBlockSynced);
            this.RemoveTransactionsAfterLastBlockSynced(wallet.LastBlockSyncedHeight, wallet.WalletId);
            this.Update(wallet);
        }

        internal void ProcessTransactions(ChainedHeader header = null)
        {
            while (true)
            {
                // Determines the HDTransactionData records that are required.
                List<HDTransactionData> hdTransactions = this.Query<HDTransactionData>($@"
                    SELECT A.WalletID
                    ,      A.AccountIndex
                    ,      A.AddressType
                    ,      A.AddressIndex
                    ,      TD.TransactionDataIndex
                    ,      A.ScriptPubKey
                    ,      A.PubKey
                    ,      T.Value
                    ,      T.OutputBlockHeight
                    ,      T.OutputBlockHash
                    ,      T.OutputTxIsCoinBase
                    ,      T.OutputTxTime
                    ,      T.OutputTxId
                    ,      T.OutputIndex
                    ,      NULL
                    ,      NULL
                    ,      NULL
                    ,      NULL
                    ,      NULL
                    ,      NULL
                    FROM   temp.TempOutput T
                    JOIN   HDAddress A
                    ON     A.ScriptPubKey = T.ScriptPubKey
                    {((header == null) ? "" : $@"
                    JOIN   HDWallet W
                    ON     W.WalletId = A.WalletId
                    AND     W.LastBlockSyncedHash = '{(header.Previous?.HashBlock ?? uint256.Zero)}'
                    ")}
                    LEFT   JOIN HDTransactionData TD
                    ON     TD.WalletId = A.WalletId
                    AND    TD.AccountIndex  = A.AccountIndex
                    AND    TD.AddressType = A.AddressType
                    AND    TD.AddressIndex = A.AddressIndex
                    AND    TD.OutputTxId = T.OutputTxId
                    AND    TD.OutputIndex = T.OutputIndex
                    WHERE  TD.OutputBlockHash IS NULL
                    AND    TD.OutputBlockHeight IS NULL
                    ORDER  BY A.WalletId, A.AccountIndex, A.AddressType, A.AddressIndex, T.OutputTxTime");

                if (hdTransactions.Count == 0)
                    break;

                var topUpRequired = new HashSet<(int walletId, int accountIndex, int addressType)>();

                // We will go through the sorted list and make some updates each time the address changes.
                (int walletId, int accountIndex, int addressType, int addressIndex) current = (-1, -1, -1, -1);
                (int walletId, int accountIndex, int addressType, int addressIndex) prev = (-1, -1, -1, -1);

                // Now go through the HDTransaction data records.
                HDAddress hdAddress = null;
                HDAccount hdAccount = null;
                bool hdAddressDirty = false;

                foreach (HDTransactionData hdTransactionData in hdTransactions)
                {
                    current = (hdTransactionData.WalletId, hdTransactionData.AccountIndex, hdTransactionData.AddressType, hdTransactionData.AddressIndex);

                    // If its a different address then update the count for the previous address.
                    if (prev.walletId != current.walletId || prev.accountIndex != current.accountIndex || prev.addressType != current.addressType || prev.addressIndex != current.addressIndex)
                    {
                        if (hdAddressDirty)
                        {
                            hdAddress.Update(this);
                            hdAddressDirty = false;
                        }

                        hdAddress = HDAddress.GetAddress(this, current.walletId, current.accountIndex, current.addressType, current.addressIndex);

                        if (prev.walletId != current.walletId || prev.accountIndex != current.accountIndex)
                            hdAccount = HDAccount.GetAccount(this, current.walletId, current.accountIndex);
                    }

                    // If its a new record.
                    if (hdTransactionData.TransactionDataIndex == null)
                    {
                        hdTransactionData.TransactionDataIndex = hdAddress.TransactionCount++;
                        hdAddressDirty = true;

                        if (!string.IsNullOrEmpty(hdAccount.ScriptPubKeyType))
                            topUpRequired.Add((hdAddress.WalletId, hdAddress.AccountIndex, hdAddress.AddressType));

                        // Insert the HDTransactionData record.
                        this.Insert(hdTransactionData);
                    }
                    else
                        this.Update(hdTransactionData);

                    prev = current;
                }

                if (hdAddressDirty)
                    hdAddress.Update(this);

                if (topUpRequired.Count == 0)
                    break;

                foreach ((int walletId, int accountIndex, int addressType) in topUpRequired)
                    this.TopUpAddresses(walletId, accountIndex, addressType);
            }

            // Update spending details on HDTransactionData records.
            this.Execute($@"
                REPLACE INTO HDTransactionData
                SELECT TD.WalletId
                ,      TD.AccountIndex
                ,      TD.AddressType
                ,      TD.AddressIndex
                ,      TD.TransactionDataIndex
                ,      TD.ScriptPubKey
                ,      TD.PubKey
                ,      TD.Value
                ,      TD.OutputBlockHeight
                ,      TD.OutputBlockHash
                ,      TD.OutputTxIsCoinBase
                ,      TD.OutputTxTime
                ,      TD.OutputTxId
                ,      TD.OutputIndex
                ,      T.SpendBlockHeight
                ,      T.SpendBlockHash
                ,      T.SpendTxTime
                ,      T.SpendTxId
                ,      (SELECT  S.ScriptPubKey
                        FROM    temp.TempOutput S
                        LEFT    JOIN HDAddress A
                        ON      A.ScriptPubKey = S.ScriptPubKey
                        WHERE   A.ScriptPubKey IS NULL
                        OR      A.AddressType = 0
                        LIMIT   1) SpendTxRecipient
                ,      T.SpendTxTotalOut
                FROM   temp.TempPrevOut T
                JOIN   HDTransactionData TD
                ON     TD.OutputTxID = T.OutputTxId
                AND    TD.OutputIndex = T.OutputIndex
                {((header == null) ? "" : $@"
                JOIN   HDWallet W
                ON     W.WalletId = TD.WalletId
                AND    W.LastBlockSyncedHash = '{(header.Previous?.HashBlock ?? uint256.Zero)}'
                ")}
                ORDER BY TD.WalletId
                ,      TD.AccountIndex
                ,      TD.AddressType
                ,      TD.AddressIndex
                ,      TD.TransactionDataIndex
                ");

            // Advance participating wallets.
            if (header != null)
            {
                this.Execute($@"
                    UPDATE HDWallet
                    SET    LastBlockSyncedHash = '{header.HashBlock}',
                            LastBlockSyncedHeight = {header.Height},
                            BlockLocator = '{string.Join(",", header.GetLocator().Blocks)}'
                    WHERE  LastBlockSyncedHash = '{(header.Previous?.HashBlock ?? uint256.Zero)}'");
            }
        }
    }
}
