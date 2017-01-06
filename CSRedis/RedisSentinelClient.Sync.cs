using CSRedis.Internal.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CSRedis
{
    public partial class RedisSentinelClient
    {
        /// <summary>
        /// Connect to the remote host
        /// </summary>
        /// <param name="timeout">Connection timeout in milliseconds</param>
        /// <returns>True if connected</returns>
        public bool Connect(int timeout)
        {
            return _connector.Connect(); // TODO: timeout
        }

        /// <summary>
        /// Call arbitrary Sentinel command (e.g. for a command not yet implemented in this library)
        /// </summary>
        /// <param name="command">The name of the command</param>
        /// <param name="args">Array of arguments to the command</param>
        /// <returns>Redis unified response</returns>
        public object Call(string command, params string[] args)
        {
            return Write(RedisCommands.Call(command, args));
        }

        T Write<T>(RedisCommand<T> command)
        {
            return _connector.Call(command);
        }

        #region sentinel
        /// <summary>
        /// Ping the Sentinel server
        /// </summary>
        /// <returns>Status code</returns>
        public string Ping()
        {
            return Write(RedisCommands.Ping());
        }

        /// <summary>
        /// Get a list of monitored Redis masters
        /// </summary>
        /// <returns>Redis master info</returns>
        public RedisMasterInfo[] Masters()
        {
            return Write(RedisCommands.Sentinel.Masters());
        }

        /// <summary>
        /// Get information on the specified Redis master
        /// </summary>
        /// <param name="masterName">Name of the Redis master</param>
        /// <returns>Master information</returns>
        public RedisMasterInfo Master(string masterName)
        {
            return Write(RedisCommands.Sentinel.Master(masterName));
        }

        /// <summary>
        /// Get a list of other Sentinels known to the current Sentinel
        /// </summary>
        /// <param name="masterName">Name of monitored master</param>
        /// <returns>Sentinel hosts and ports</returns>
        public RedisSentinelInfo[] Sentinels(string masterName)
        {
            return Write(RedisCommands.Sentinel.Sentinels(masterName));
        }

        /// <summary>
        /// Get a list of monitored Redis slaves to the given master 
        /// </summary>
        /// <param name="masterName">Name of monitored master</param>
        /// <returns>Redis slave info</returns>
        public RedisSlaveInfo[] Slaves(string masterName)
        {
            return Write(RedisCommands.Sentinel.Slaves(masterName));
        }

        /// <summary>
        /// Get the IP and port of the current master Redis server
        /// </summary>
        /// <param name="masterName">Name of monitored master</param>
        /// <returns>IP and port of master Redis server</returns>
        public Tuple<string, int> GetMasterAddrByName(string masterName)
        {
            return Write(RedisCommands.Sentinel.GetMasterAddrByName(masterName));
        }

        /// <summary>
        /// Open one or more subscription channels to Redis Sentinel server
        /// </summary>
        /// <param name="channels">Name of channels to open (refer to http://redis.io/ for channel names)</param>
        public void Subscribe(params string[] channels)
        {
            _subscription.Send(RedisCommands.Subscribe(channels));
        }

        /// <summary>
        /// Close one or more subscription channels to Redis Sentinel server
        /// </summary>
        /// <param name="channels">Name of channels to close</param>
        public void Unsubscribe(params string[] channels)
        {
            _subscription.Send(RedisCommands.Unsubscribe(channels));
        }

        /// <summary>
        /// Open one or more subscription channels to Redis Sentinel server
        /// </summary>
        /// <param name="channelPatterns">Pattern of channels to open (refer to http://redis.io/ for channel names)</param>
        public void PSubscribe(params string[] channelPatterns)
        {
            _subscription.Send(RedisCommands.PSubscribe(channelPatterns));
        }

        /// <summary>
        /// Close one or more subscription channels to Redis Sentinel server
        /// </summary>
        /// <param name="channelPatterns">Pattern of channels to close</param>
        public void PUnsubscribe(params string[] channelPatterns)
        {
            _subscription.Send(RedisCommands.PUnsubscribe(channelPatterns));
        }

        /// <summary>
        /// Get master state information
        /// </summary>
        /// <param name="ip">Host IP</param>
        /// <param name="port">Host port</param>
        /// <param name="currentEpoch">Current epoch</param>
        /// <param name="runId">Run ID</param>
        /// <returns>Master state</returns>
        public RedisMasterState IsMasterDownByAddr(string ip, int port, long currentEpoch, string runId)
        {
            return Write(RedisCommands.Sentinel.IsMasterDownByAddr(ip, port, currentEpoch, runId));
        }

        /// <summary>
        /// Clear state in all masters with matching name
        /// </summary>
        /// <param name="pattern">Master name pattern</param>
        /// <returns>Number of masters that were reset</returns>
        public long Reset(string pattern)
        {
            return Write(RedisCommands.Sentinel.Reset(pattern));
        }

        /// <summary>
        /// Force a failover as if the master was not reachable, and without asking for agreement from other sentinels
        /// </summary>
        /// <param name="masterName">Master name</param>
        /// <returns>Status code</returns>
        public string Failover(string masterName)
        {
            return Write(RedisCommands.Sentinel.Failover(masterName));
        }

        /// <summary>
        /// Start monitoring a new master
        /// </summary>
        /// <param name="name">Master name</param>
        /// <param name="port">Master port</param>
        /// <param name="quorum">Quorum count</param>
        /// <returns>Status code</returns>
        public string Monitor(string name, int port, int quorum)
        {
            return Write(RedisCommands.Sentinel.Monitor(name, port, quorum));
        }

        /// <summary>
        /// Remove the specified master
        /// </summary>
        /// <param name="name">Master name</param>
        /// <returns>Status code</returns>
        public string Remove(string name)
        {
            return Write(RedisCommands.Sentinel.Remove(name));
        }

        /// <summary>
        /// Change configuration parameters of a specific master
        /// </summary>
        /// <param name="masterName">Master name</param>
        /// <param name="option">Config option name</param>
        /// <param name="value">Config option value</param>
        /// <returns>Status code</returns>
        public string Set(string masterName, string option, string value)
        {
            return Write(RedisCommands.Sentinel.Set(masterName, option, value));
        }
        #endregion
    }
}
