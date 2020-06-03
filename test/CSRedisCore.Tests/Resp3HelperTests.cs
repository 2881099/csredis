using CSRedis.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CSRedisCore.Tests {
	public class Resp3HelperTests {

		public class RedisSocket : IDisposable
		{
			Socket _socket;
			public NetworkStream Stream { get; }

			public RedisSocket(Socket socket)
			{
				_socket = socket;
				Stream = new NetworkStream(_socket, true);
			}
			public void Dispose()
			{
				_socket.Shutdown(SocketShutdown.Both);
				_socket.Close();
				_socket.Dispose();
			}

			public static RedisSocket GetRedisSocket()
			{
				var endpoint = new IPEndPoint(IPAddress.Parse("192.168.164.10"), 6379);
				var _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
				_socket.Connect(endpoint);
				return new RedisSocket(_socket);
			}
		}

		static object[] PrepareCmd(string cmd, string subcmd = null, params object[] parms)
		{
			if (string.IsNullOrWhiteSpace(cmd)) throw new ArgumentNullException("Redis command not is null or empty.");
			object[] args = null;
			if (parms?.Any() != true)
			{
				if (string.IsNullOrWhiteSpace(subcmd) == false) args = new object[] { cmd, subcmd };
				else args = cmd.Split(' ').Where(a => string.IsNullOrWhiteSpace(a) == false).ToArray();
			}
			else
			{
				var issubcmd = string.IsNullOrWhiteSpace(subcmd) == false;
				args = new object[parms.Length + 1 + (issubcmd ? 1 : 0)];
				var argsIdx = 0;
				args[argsIdx++] = cmd;
				if (issubcmd) args[argsIdx++] = subcmd;
				foreach (var prm in parms) args[argsIdx++] = prm;
			}
			return args;
		}
		static Resp3Helper.ReadResult<T> ExecCmd<T>(string cmd, string subcmd = null, params object[] parms)
		{
			var args = PrepareCmd(cmd, subcmd, parms);
			using (var rds = RedisSocket.GetRedisSocket())
			{
				Resp3Helper.Write(rds.Stream, args, true);
				var rt = Resp3Helper.Read<T>(rds.Stream);
				return rt;
			}
		}
		static ExecCmdListenResult ExecCmdListen(Action<ExecCmdListenResult, string> ondata, string cmd, string subcmd = null, params object[] parms)
		{
			var args = PrepareCmd(cmd, subcmd, parms);
			var rds = RedisSocket.GetRedisSocket();
			Resp3Helper.Write(rds.Stream, args, true);
			var rd = Resp3Helper.Read<string>(rds.Stream);
			var rt = new ExecCmdListenResult { rds = rds };
			new Thread(() =>
			{
				ondata?.Invoke(rt, rd.Value);
				while (rt._running)
				{
					try
					{
						ondata?.Invoke(rt, Resp3Helper.Read<string>(rds.Stream).Value);
					}
					catch(Exception ex)
					{
						Console.WriteLine(ex.Message);
					}
				}
			}).Start();
			return rt;
		}
		public class ExecCmdListenResult : IDisposable
		{
			internal RedisSocket rds;
			internal bool _running = true;

			public void Dispose() => _running = false;
		}

		class RedisCommand
		{
			class RedisServerException : Exception
			{
				public RedisServerException(string message) : base(message) { }
			}
			public Resp3Helper.ReadResult<string[]> AclCat(string categoryname = null) => string.IsNullOrWhiteSpace(categoryname) ? ExecCmd<string[]>("ACL", "CAT") : ExecCmd<string[]>("ACL", "CAT", categoryname);
			public Resp3Helper.ReadResult<int> AclDelUser(params string[] username) => username?.Any() == true ? ExecCmd<int>("ACL", "DELUSER", username) : throw new ArgumentException(nameof(username));
			public Resp3Helper.ReadResult<string> AclGenPass(int bits = 0) => bits <= 0 ? ExecCmd<string>("ACL", "GENPASS") : ExecCmd<string>("ACL", "GENPASS", bits);
			public Resp3Helper.ReadResult<string[]> AclList() => ExecCmd<string[]>("ACL", "LIST");
			public Resp3Helper.ReadResult<string> AclLoad() => ExecCmd<string>("ACL", "LOAD");
			public Resp3Helper.ReadResult<LogInfo[]> AclLog(long count = 0) => (count <= 0 ? ExecCmd<object[][]>("ACL", "LOG") : ExecCmd<object[][]>("ACL", "LOG", count)).NewValue(x => x.Select(a => a.MapToClass<LogInfo>()).ToArray());
			public class LogInfo { public long Count { get; } public string Reason { get; } public string Context { get; } public string Object { get; } public string Username { get; } public decimal AgeSeconds { get; } public string ClientInfo { get; } }
			public Resp3Helper.ReadResult<string> AclSave() => ExecCmd<string>("ACL", "SAVE");
			public Resp3Helper.ReadResult<string> AclSetUser(params string[] rule) => rule?.Any() == true ? ExecCmd<string>("ACL", "SETUSER", rule) : throw new ArgumentException(nameof(rule));
			public Resp3Helper.ReadResult<string[]> AclUsers() => ExecCmd<string[]>("ACL", "USERS");
			public Resp3Helper.ReadResult<string> AclWhoami() => ExecCmd<string>("ACL", "WHOAMI");
			public Resp3Helper.ReadResult<string> BgRewriteAof() => ExecCmd<string>("BGREWRITEAOF");
			public Resp3Helper.ReadResult<string> BgSave(string schedule = null) => ExecCmd<string>("BGSAVE", schedule);
			public Resp3Helper.ReadResult<object[]> Command() => ExecCmd<object[]>("COMMAND");
			public Resp3Helper.ReadResult<int> CommandCount() => ExecCmd<int>("COMMAND", "COUNT");
			public Resp3Helper.ReadResult<string[]> CommandGetKeys(params string[] command) => command?.Any() == true ? ExecCmd<string[]>("COMMAND", "GETKEYS", command) : throw new ArgumentException(nameof(command));
			public Resp3Helper.ReadResult<string[]> CommandInfo(params string[] command) => command?.Any() == true ? ExecCmd<string[]>("COMMAND", "INFO", command) : throw new ArgumentException(nameof(command));
			public Resp3Helper.ReadResult<Dictionary<string, string>> ConfigGet(string parameter) => ExecCmd<string[]>("CONFIG", "GET", parameter).NewValue(a => a.MapToHash<string>());
			public Resp3Helper.ReadResult<string> ConfigResetStat() => ExecCmd<string>("CONFIG", "RESETSTAT");
			public Resp3Helper.ReadResult<string> ConfigRewrite() => ExecCmd<string>("CONFIG", "REWRITE");
			public Resp3Helper.ReadResult<string> ConfigSet(string parameter, object value) => ExecCmd<string>("CONFIG", "SET", parameter, value);
			public Resp3Helper.ReadResult<long> DbSize() => ExecCmd<long>("DBSIZE");
			public Resp3Helper.ReadResult<string> DebugObject(string key) => ExecCmd<string>("DEBUG", "OBJECT", key);
			public Resp3Helper.ReadResult<string> DebugSegfault() => ExecCmd<string>("DEBUG", "SEGFAULT");
			public Resp3Helper.ReadResult<string> FlushAll(bool isasync = false) => ExecCmd<string>("FLUSHALL", isasync ? "ASYNC" : null);
			public Resp3Helper.ReadResult<string> FlushDb(bool isasync = false) => ExecCmd<string>("FLUSHDB", isasync ? "ASYNC" : null);
			public Resp3Helper.ReadResult<string> Info(string section = null) => ExecCmd<string>("INFO", section);
			public Resp3Helper.ReadResult<long> LastSave() => ExecCmd<long>("LASTSAVE");
			public Resp3Helper.ReadResult<string> LatencyDoctor() => ExecCmd<string>("LATENCY", "DOCTOR");
			public Resp3Helper.ReadResult<string> LatencyGraph(string @event) => ExecCmd<string>("LATENCY", "GRAPH", @event);
			public Resp3Helper.ReadResult<string[]> LatencyHelp() => ExecCmd<string[]>("LATENCY", "HELP");
			public Resp3Helper.ReadResult<string[][]> LatencyHistory(string @event) => ExecCmd<string[][]>("HISTORY", "HELP", @event);
			public Resp3Helper.ReadResult<string[][]> LatencyLatest() => ExecCmd<string[][]>("HISTORY", "LATEST");
			public Resp3Helper.ReadResult<long> LatencyReset(string @event) => ExecCmd<long>("LASTSAVE", "RESET", @event);
			public Resp3Helper.ReadResult<string> Lolwut(string version) => ExecCmd<string>("LATENCY", string.IsNullOrWhiteSpace(version) ? null : $"VERSION {version}");
			public Resp3Helper.ReadResult<string> MemoryDoctor() => ExecCmd<string>("MEMORY", "DOCTOR");
			public Resp3Helper.ReadResult<string[]> MemoryHelp() => ExecCmd<string[]>("MEMORY", "HELP");
			public Resp3Helper.ReadResult<string> MemoryMallocStats() => ExecCmd<string>("MEMORY", "MALLOC-STATS");
			public Resp3Helper.ReadResult<string> MemoryPurge() => ExecCmd<string>("MEMORY", "PURGE");
			public Resp3Helper.ReadResult<Dictionary<string, string>> MemoryStats() => ExecCmd<string[]>("MEMORY", "STATS").NewValue(a => a.MapToHash<string>());
			public Resp3Helper.ReadResult<long> MemoryUsage(string key, long count = 0) => count <= 0 ? ExecCmd<long>("MEMORY ", "USAGE", key) : ExecCmd<long>("MEMORY ", "USAGE", key, "SAMPLES", count);
			public Resp3Helper.ReadResult<string[][]> ModuleList() => ExecCmd<string[][]>("MODULE", "LIST");
			public Resp3Helper.ReadResult<string> ModuleLoad(string path, params string[] args) => args?.Any() == true ? ExecCmd<string>("MODULE", "LOAD", new[] { path }.Concat(args)) : ExecCmd<string>("MODULE", "LOAD", path);
			public Resp3Helper.ReadResult<string> ModuleUnload(string name) => ExecCmd<string>("MODULE", "UNLOAD", name);
			public ExecCmdListenResult Monitor(Action<ExecCmdListenResult, string> onData) => ExecCmdListen(onData, "MONITOR");
			public ExecCmdListenResult Psync(string replicationid, string offset, Action<ExecCmdListenResult, string> onData) => ExecCmdListen(onData, "PSYNC", replicationid, offset);
			public Resp3Helper.ReadResult<string> ReplicaOf(string host, int port) => ExecCmd<string>("REPLICAOF", host, port);
			public Resp3Helper.ReadResult<object> Role() => ExecCmd<object>("ROLE");
			public Resp3Helper.ReadResult<string> Save() => ExecCmd<string>("SAVE");
			public Resp3Helper.ReadResult<string> Shutdown(bool save) => ExecCmd<string>("SHUTDOWN", save ? "SAVE" : "NOSAVE");
			public Resp3Helper.ReadResult<string> SlaveOf(string host, int port) => ExecCmd<string>("SLAVEOF", host, port);
			public Resp3Helper.ReadResult<object> SlowLog(string subcommand, params string[] argument) => ExecCmd<object>("SLOWLOG", subcommand, argument);
			public Resp3Helper.ReadResult<string> SwapDb(int index1, int index2) => ExecCmd<string>("SWAPDB", null, index1, index2);
			public ExecCmdListenResult Sync(Action<ExecCmdListenResult, string> onData) => ExecCmdListen(onData, "SYNC");
			public Resp3Helper.ReadResult<DateTime> Time() => ExecCmd<long[]>("TIME").NewValue(a => new DateTime(1970, 0, 0).AddSeconds(a[0]).AddTicks(a[1] * 10));

		}
		RedisCommand rds { get; } = new RedisCommand();

		#region server test
		[Fact]
		public void BgRewriteAof()
		{
			var rt = rds.BgRewriteAof();
			if (!rt.IsError) rt.Value.AssertEqual("Background append only file rewriting started");
		}
		[Fact]
		public void BgSave()
		{
			var rt = rds.BgSave();
			if (!rt.IsError) rt.Value.AssertEqual("Background saving started");
		}
		[Fact]
		public void Command()
		{
			string UFString(string text)
			{
				if (text.Length <= 1) return text.ToUpper();
				else return text.Substring(0, 1).ToUpper() + text.Substring(1, text.Length - 1);
			}

			var rt = rds.Command();
			var sb = string.Join("\r\n\r\n", (rt.Value).OrderBy(a1 => (a1 as List<object>)[0].ToString()).Select(a1 =>
			{
				var a = a1 as List<object>;
				var plen = int.Parse(a[1].ToString());
				var firstKey = int.Parse(a[3].ToString());
				var lastKey = int.Parse(a[4].ToString());
				var stepCount = int.Parse(a[5].ToString());
				var parms = "";
				if (plen > 1)
				{
					for (var x = 1; x < plen; x++)
					{
						if (x == firstKey) parms += "string key, ";
						else parms += "string parm, ";
					}
					parms = parms.Remove(parms.Length - 2);
				}
				if (plen < 0)
				{
					for (var x = 1; x < -plen; x++)
					{
						if (x == firstKey) parms += "string key, ";
						else parms += "string parm, ";
					}
					if (parms.Length > 0)
						parms = parms.Remove(parms.Length - 2);
				}
				
				return $@"
//{string.Join(", ", a[2] as List<object>)}
//{string.Join(", ", a[6] as List<object>)}
public void {UFString(a[0].ToString())}({parms}) {{ }}";
			}));
		}
		[Fact]
		public void CommandCount()
		{
			var rt = rds.CommandCount();
			if (!rt.IsError) (rt.Value > 100).AssertEqual(true);
		}
		[Fact]
		public void CommandGetKeys()
		{
			var rt = rds.CommandGetKeys("MSET", "a", "b", "c", "d", "e", "f");
			if (!rt.IsError)
			{
				rt.Value[0].AssertEqual("a");
				rt.Value[1].AssertEqual("c");
				rt.Value[2].AssertEqual("e");
			}
		}
		[Fact]
		public void ConfigGet()
		{
			var rt = rds.ConfigGet("*max-*-entries*");
			if (!rt.IsError)
			{
				rt.Value.ContainsKey("hash-max-ziplist-entries").AssertEqual(true);
				rt.Value.ContainsKey("set-max-intset-entries").AssertEqual(true);
				rt.Value.ContainsKey("zset-max-ziplist-entries").AssertEqual(true);
			}
		}
		[Fact]
		public void ConfigResetStat()
		{
			var rt = rds.ConfigResetStat();
			if (!rt.IsError) rt.Value.AssertEqual("OK");
		}
		[Fact]
		public void ConfigRewrite()
		{
			var rt = rds.ConfigRewrite();
			if (!rt.IsError) rt.Value.AssertEqual("OK");
		}
		[Fact]
		public void ConfigSet()
		{
			var rt = rds.ConfigSet("hash-max-ziplist-entries", 512);
			if (!rt.IsError) rt.Value.AssertEqual("OK");
		}
		[Fact]
		public void DbSize()
		{
			var rt = rds.DbSize();
			if (!rt.IsError) (rt.Value >= 0).AssertEqual(true);
		}
		[Fact]
		public void DebugObject()
		{
			var rt = rds.ConfigSet("hash-max-ziplist-entries", 512);
			if (!rt.IsError) rt.Value.AssertEqual("Value at:");
			//Value at:0x7f52b584aa80 refcount:2147483647 encoding:int serializedlength:2 lru:12199791 lru_seconds_idle:40537
		}
		[Fact]
		public void LastSave()
		{
			var rt = rds.LastSave();
			if (!rt.IsError) (rt.Value >= 0).AssertEqual(true);
		}
		[Fact]
		public void LatencyHelp()
		{
			var rt = rds.LatencyHelp();
			if (!rt.IsError) (rt.Value.Length > 0).AssertEqual(true);
		}
		[Fact]
		public void MemoryStats()
		{
			var rt = rds.MemoryStats();
			if (!rt.IsError) rt.Value.ContainsKey("keys.count").AssertEqual(true);
		}
		[Fact]
		public void MemoryUsage()
		{
			var rt = rds.MemoryUsage("key");
			if (!rt.IsError) (rt.Value > 0).AssertEqual(true);
		}
		#endregion

		#region acl test
		[Fact]
		public void AclCat()
		{
			var assertList = new[] { "keyspace", "read", "write", "set", "sortedset", "list", "hash", "string", "bitmap", "hyperloglog", "geo", "stream", "pubsub", "admin", "fast", "slow", "blocking", "dangerous", "connection", "transaction", "scripting" };
			var rt = rds.AclCat();
			if (!rt.IsError) assertList.Where(a => rt.Value.Contains(a)).Count().AssertEqual(assertList.Length);
			assertList = new[] { "flushdb", "lastsave", "info", "latency", "slowlog", "replconf", "slaveof", "acl", "flushall", "role", "pfdebug", "cluster", "shutdown", "restore-asking", "sort", "sync", "pfselftest", "restore", "swapdb", "config", "keys", "psync", "migrate", "bgsave", "monitor", "bgrewriteaof", "module", "debug", "save", "client", "replicaof" };
			rt = rds.AclCat("dangerous");
			if (!rt.IsError) assertList.Where(a => rt.Value.Contains(a)).Count().AssertEqual(assertList.Length);
		}
		[Fact]
		public void AclDelUser()
		{
			var rt = rds.AclDelUser("antirez");
			if (!rt.IsError) rt.Value.AssertEqual(0);
		}
		[Fact]
		public void AclGenPass()
		{
			var rt = rds.AclGenPass();
			if (!rt.IsError) rt.Value.ToString().Length.AssertEqual(64);
			rt = rds.AclGenPass(32);
			if (!rt.IsError) rt.Value.ToString().Length.AssertEqual(8);
			rt = rds.AclGenPass(5);
			if (!rt.IsError) rt.Value.ToString().Length.AssertEqual(2);
		}
		[Fact]
		public void AclList()
		{
			//1) "user default on nopass ~* +@all"
			//2) "user karin on +@all -@admin -@dangerous"
			//1) "user antirez on #9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08 ~objects:* +@all -@admin -@dangerous"
			//2) "user default on nopass ~* +@all"
			var rt = rds.AclList();
			if (!rt.IsError) rt.Value[0].StartsWith("user ").AssertEqual(true);
		}
		[Fact]
		public void AclLoad()
		{
			var rt = rds.AclLoad();
			if (!rt.IsError) rt.Value.AssertEqual("OK");
			//rt.Value.ToString().StartsWith("ERR This Redis instance is not configured to use an ACL file.");
		}
		[Fact]
		public void AclLog()
		{
			//127.0.0.1:6379> acl log 1
			//1) 1# "count" => (integer) 1
			//   2# "reason" => "auth"
			//   3# "context" => "toplevel"
			//   4# "object" => "AUTH"
			//   5# "username" => "someuser"
			//   6# "age-seconds" => (double) 8.3040000000000003
			//   7# "client-info" => "id=8 addr=127.0.0.1:40298 fd=8 name= age=6802 idle=0 flags=N db=0 sub=0 psub=0 multi=-1 qbuf=48 qbuf-free=32720 obl=0 oll=0 omem=0 events=r cmd=auth user=default"
			ExecCmd<string>("AUTH someuser wrongpassword");
			var rt = rds.AclLog();
			if (!rt.IsError) rt.Value.AssertEqual("OK");
		}
		[Fact]
		public void AclSave()
		{
			var rt = rds.AclSave();
			if (!rt.IsError) rt.Value.AssertEqual("OK");
			//rt.Value.ToString().StartsWith("ERR This Redis instance is not configured to use an ACL file.");
		}
		[Fact]
		public void AclSetUser()
		{
			var rt = rds.AclSetUser("karin", "on", "+@all", "-@dangerous");
			if (!rt.IsError) rt.Value.AssertEqual("OK");
		}
		[Fact]
		public void AclUsers()
		{
			var rt = rds.AclUsers();
			if (!rt.IsError) rt.Value.Contains("default").AssertEqual(true);
		}
		[Fact]
		public void AclWhoami()
		{
			var rt = rds.AclWhoami();
			if (!rt.IsError) rt.Value.AssertEqual("default");
		}
        #endregion


  //      [Fact]
		//public void Set()
		//{
		//	var val = Guid.NewGuid().ToString();
		//	ExecCmd("SET", "test01", val).AssertEqual("OK");
		//	ExecCmd("GET", "test01").AssertEqual(val);
		//	ExecCmd("SET", "test02", Encoding.UTF8.GetBytes(val)).AssertEqual("OK");
		//	ExecCmd("SET", "test02", val).AssertEqual(val);
		//}
	}

	static class TestExntesions
	{
		public static void AssertEqual(this object obj, object val) => Assert.Equal(val, obj);
	}
}
