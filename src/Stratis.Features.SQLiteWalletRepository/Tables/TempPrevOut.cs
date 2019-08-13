﻿namespace Stratis.Features.SQLiteWalletRepository.Tables
{
    internal class TempPrevOut : TempRow
    {
        public string OutputTxId { get; set; }
        public int OutputIndex { get; set; }
        public int SpendBlockHeight { get; set; }
        public string SpendBlockHash { get; set; }
        public int SpendTxTime { get; set; }
        public string SpendTxId { get; set; }
        public decimal SpendTxTotalOut { get; set; }

        public TempPrevOut() : base() { }
    }
}
