﻿using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Stratis.Bitcoin
{
	public class DBreezeNoSqlRepository : NoSqlRepository, IDisposable
	{
		DBreezeSingleThreadSession _Session;
		string _Name;
		public DBreezeNoSqlRepository(string name, string folder)
		{
			_Name = name;
			_Session = new DBreezeSingleThreadSession(name, folder);
			_Session.Do(() =>
			{
				_Session.Transaction.ValuesLazyLoadingIsOn = false;
			});
		}

		public void Dispose()
		{
			_Session.Dispose();
		}

		protected override Task<byte[]> GetBytes(string key)
		{
			return _Session.Do(() =>
			{
				var row = _Session.Transaction.Select<string, byte[]>(_Name, key);
				if(row == null || row.Exists)
					return null;
				return row.Value;
			});
		}
		protected override Task PutBytes(string key, byte[] data)
		{
			return _Session.Do(() =>
			{
				_Session.Transaction.Insert(_Name, key, data);
				_Session.Transaction.Commit();
			});
		}

		protected override async Task PutBytesBatch(IEnumerable<Tuple<string, byte[]>> enumerable)
		{
			foreach(var kv in enumerable)
			{
				await PutBytes(kv.Item1, kv.Item2).ConfigureAwait(false);
			}
		}
	}
}
