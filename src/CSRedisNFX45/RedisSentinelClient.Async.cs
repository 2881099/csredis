using CSRedis.Internal.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSRedis
{
    public partial class RedisSentinelClient
    {
        /// <summary>
        /// Connect to the remote host
        /// </summary>
        /// <returns>True if connected</returns>
        public Task<bool> ConnectAsync()
        {
            return _connector.ConnectAsync();
        }

        /// <summary>
        /// Call arbitrary Sentinel command (e.g. for a command not yet implemented in this library)
        /// </summary>
        /// <param name="command">The name of the command</param>
        /// <param name="args">Array of arguments to the command</param>
        /// <returns>Redis unified response</returns>
        public Task<object> CallAsync(string command, params string[] args)
        {
            return WriteAsync(new RedisObject(command, args));
        }

        Task<T> WriteAsync<T>(RedisCommand<T> command)
        {
            return _connector.CallAsync(command);
        }

        #region sentinel
        /// <summary>
        /// Ping the Sentinel server
        /// </summary>
        /// <returns>Status code</returns>
        public Task<string> PingAsync()
        {
            return WriteAsync(RedisCommands.Ping());
        }

        /// <summary>
        /// Get a list of monitored Redis masters
        /// </summary>
        /// <returns>Redis master info</returns>
        public Task<RedisMasterInfo[]> MastersAsync()
        {
            return WriteAsync(RedisCommands.Sentinel.Masters());
        }

        /// <summary>
        /// Get information on the specified Redis master
        /// </summary>
        /// <param name="masterName">Name of the Redis master</param>
        /// <returns>Master information</returns>
        public Task<RedisMasterInfo> MasterAsync(string masterName)
        {
            return WriteAsync(RedisCommands.Sentinel.Master(masterName));
        }

        /// <summary>
        /// Get a list of other Sentinels known to the current Sentinel
        /// </summary>
        /// <param name="masterName">Name of monitored master</param>
        /// <returns>Sentinel hosts and ports</returns>
        public Task<RedisSentinelInfo[]> SentinelsAsync(string masterName)
        {
            return WriteAsync(RedisCommands.Sentinel.Sentinels(masterName));
        }


        /// <summary>
        /// Get a list of monitored Redis slaves to the given master 
        /// </summary>
        /// <param name="masterName">Name of monitored master</param>
        /// <returns>Redis slave info</returns>
        public Task<RedisSlaveInfo[]> SlavesAsync(string masterName)
        {
            return WriteAsync(RedisCommands.Sentinel.Slaves(masterName));
        }

        /// <summary>
        /// Get the IP and port of the current master Redis server
        /// </summary>
        /// <param name="masterName">Name of monitored master</param>
        /// <returns>IP and port of master Redis server</returns>
        public Task<Tuple<string, int>> GetMasterAddrByNameAsync(string masterName)
        {
            return WriteAsync(RedisCommands.Sentinel.GetMasterAddrByName(masterName));
        }

        /// <summary>
        /// Get master state information
        /// </summary>
        /// <param name="ip">Host IP</param>
        /// <param name="port">Host port</param>
        /// <param name="currentEpoch">Current epoch</param>
        /// <param name="runId">Run ID</param>
        /// <returns>Master state</returns>
        public Task<RedisMasterState> IsMasterDownByAddrAsync(string ip, int port, long currentEpoch, string runId)
        {
            return WriteAsync(RedisCommands.Sentinel.IsMasterDownByAddr(ip, port, currentEpoch, runId));
        }

        /// <summary>
        /// Clear state in all masters with matching name
        /// </summary>
        /// <param name="pattern">Master name pattern</param>
        /// <returns>Number of masters that were reset</returns>
        public Task<long> ResetAsync(string pattern)
        {
            return WriteAsync(RedisCommands.Sentinel.Reset(pattern));
        }

        /// <summary>
        /// Force a failover as if the master was not reachable, and without asking for agreement from other sentinels
        /// </summary>
        /// <param name="masterName">Master name</param>
        /// <returns>Status code</returns>
        public Task<string> FailoverAsync(string masterName)
        {
            return WriteAsync(RedisCommands.Sentinel.Failover(masterName));
        }

        /// <summary>
        /// Start monitoring a new master
        /// </summary>
        /// <param name="name">Master name</param>
        /// <param name="port">Master port</param>
        /// <param name="quorum">Quorum count</param>
        /// <returns>Status code</returns>
        public Task<string> MonitorAsync(string name, int port, int quorum)
        {
            return WriteAsync(RedisCommands.Sentinel.Monitor(name, port, quorum));
        }

        /// <summary>
        /// Remove the specified master
        /// </summary>
        /// <param name="name">Master name</param>
        /// <returns>Status code</returns>
        public Task<string> RemoveAsync(string name)
        {
            return WriteAsync(RedisCommands.Sentinel.Remove(name));
        }

        /// <summary>
        /// Change configuration parameters of a specific master
        /// </summary>
        /// <param name="masterName">Master name</param>
        /// <param name="option">Config option name</param>
        /// <param name="value">Config option value</param>
        /// <returns>Status code</returns>
        public Task<string> SetAsync(string masterName, string option, string value)
        {
            return WriteAsync(RedisCommands.Sentinel.Set(masterName, option, value));
        }
        #endregion
    }
}
