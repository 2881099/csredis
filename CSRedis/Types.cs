
using System;
using System.Runtime.Serialization;
namespace CSRedis
{
    /// <summary>
    /// Sub-command used by Redis OBJECT command
    /// </summary>
    public enum RedisObjectSubCommand
    {
        /// <summary>
        /// Return the number of references of the value associated with the specified key
        /// </summary>
        RefCount,

        /// <summary>
        /// Return the number of seconds since the object stored at the specified key is idle
        /// </summary>
        IdleTime,
    };

    /// <summary>
    /// Sort direction used by Redis SORT command
    /// </summary>
    public enum RedisSortDir
    {
        /// <summary>
        /// Sort ascending (a-z)
        /// </summary>
        Asc,

        /// <summary>
        /// Sort descending (z-a)
        /// </summary>
        Desc,
    }

    /// <summary>
    /// Insert position used by Redis LINSERT command
    /// </summary>
    public enum RedisInsert
    {
        /// <summary>
        /// Insert before pivot element
        /// </summary>
        Before,

        /// <summary>
        /// Insert after pivot element
        /// </summary>
        After,
    }

    /// <summary>
    /// Operation used by Redis BITOP command
    /// </summary>
    public enum RedisBitOp
    {
        /// <summary>
        /// Bitwise AND
        /// </summary>
        And,

        /// <summary>
        /// Bitwise OR
        /// </summary>
        Or,

        /// <summary>
        /// Bitwise EXCLUSIVE-OR
        /// </summary>
        XOr,

        /// <summary>
        /// Bitwise NOT
        /// </summary>
        Not,
    }

    /// <summary>
    /// Aggregation function used by Reids set operations
    /// </summary>
    public enum RedisAggregate
    {
        /// <summary>
        /// Aggregate SUM
        /// </summary>
        Sum,

        /// <summary>
        /// Aggregate MIN
        /// </summary>
        Min,

        /// <summary>
        /// Aggregate MAX
        /// </summary>
        Max,
    }

    /// <summary>
    /// Redis unified message prefix
    /// </summary>
    public enum RedisMessage
    {
        /// <summary>
        /// Error message
        /// </summary>
        Error = '-',

        /// <summary>
        /// Status message
        /// </summary>
        Status = '+',

        /// <summary>
        /// Bulk message
        /// </summary>
        Bulk = '$',

        /// <summary>
        /// Multi bulk message
        /// </summary>
        MultiBulk = '*',

        /// <summary>
        /// Int message
        /// </summary>
        Int = ':',
    }

    /// <summary>
    /// Redis subscription response type
    /// </summary>
    public enum RedisSubscriptionResponseType
    {
        /// <summary>
        /// Channel subscribed
        /// </summary>
        Subscribe,

        /// <summary>
        /// Message published
        /// </summary>
        Message,

        /// <summary>
        /// Channel unsubscribed
        /// </summary>
        Unsubscribe,

        /// <summary>
        /// Channel pattern subscribed
        /// </summary>
        PSubscribe,

        /// <summary>
        /// Message published to channel pattern
        /// </summary>
        PMessage,

        /// <summary>
        /// Channel pattern unsubsribed
        /// </summary>
        PUnsubscribe,
    }

    /// <summary>
    /// Redis existence specification for SET command
    /// </summary>
    public enum RedisExistence 
    { 
        /// <summary>
        /// Only set the key if it does not already exist
        /// </summary>
        Nx, 

        /// <summary>
        /// Only set the key if it already exists
        /// </summary>
        Xx,
    }

    /// <summary>
    /// Base class for Redis role information
    /// </summary>
    public abstract class RedisRole
    {
        readonly string _roleName;
        
        /// <summary>
        /// Get the role type
        /// </summary>
        public string RoleName { get { return _roleName; } }
        
        internal RedisRole(string roleName)
        {
            _roleName = roleName;
        }
    }

    /// <summary>
    /// Represents information on the Redis master role
    /// </summary>
    public class RedisMasterRole : RedisRole
    {
        readonly long _replicationOffset;
        readonly Tuple<string, int, int>[] _slaves;

        /// <summary>
        /// Get the master replication offset
        /// </summary>
        public long ReplicationOffset { get { return _replicationOffset; } }

        /// <summary>
        /// Get the slaves associated with the current master
        /// </summary>
        public Tuple<string, int, int>[] Slaves { get { return _slaves; } }

        internal RedisMasterRole(string role, long replicationOffset, Tuple<string, int, int>[] slaves)
            : base(role)
        {
            _replicationOffset = replicationOffset;
            _slaves = slaves;
        }
    }

    /// <summary>
    /// Represents information on the Redis slave role
    /// </summary>
    public class RedisSlaveRole : RedisRole
    {
        readonly string _masterIp;
        readonly int _masterPort;
        readonly string _replicationState;
        readonly long _dataReceived;

        /// <summary>
        /// Get the IP address of the master node
        /// </summary>
        public string MasterIp { get { return _masterIp; } }

        /// <summary>
        /// Get the port of the master node
        /// </summary>
        public int MasterPort { get { return _masterPort; } }

        /// <summary>
        /// Get the replication state
        /// </summary>
        public string ReplicationState { get { return _replicationState; } }

        /// <summary>
        /// Get the number of bytes received
        /// </summary>
        public long DataReceived { get { return _dataReceived; } }

        internal RedisSlaveRole(string role, string masterIp, int masterPort, string replicationState, long dataReceived)
            : base(role)
        {
            _masterIp = masterIp;
            _masterPort = masterPort;
            _replicationState = replicationState;
            _dataReceived = dataReceived;
        }
    }

    /// <summary>
    /// Represents information on the Redis sentinel role
    /// </summary>
    public class RedisSentinelRole : RedisRole
    {
        readonly string[] _masters;

        /// <summary>
        /// Get the masters known to the current Sentinel
        /// </summary>
        public string[] Masters { get { return _masters; } }

        internal RedisSentinelRole(string role, string[] masters)
            : base(role)
        {
            _masters = masters;
        }
    }

    /// <summary>
    /// Represents the result of a Redis SCAN or SSCAN operation
    /// </summary>
    public class RedisScan<T>
    {
        readonly long _cursor;
        readonly T[] _items;

        /// <summary>
        /// Updated cursor that should be used as the cursor argument in the next call
        /// </summary>
        public long Cursor { get { return _cursor; } }

        /// <summary>
        /// Collection of elements returned by the SCAN operation
        /// </summary>
        public T[] Items { get { return _items; } }

        internal RedisScan(long cursor, T[] items)
        {
            _cursor = cursor;
            _items = items;
        }
    }

    /// <summary>
    /// Represents a Redis subscription response
    /// </summary>
    public class RedisSubscriptionResponse
    {
        readonly string _channel;
        readonly string _pattern;
        readonly string _type;

        /// <summary>
        /// Get the subscription channel name
        /// </summary>
        public string Channel { get { return _channel; } }

        /// <summary>
        /// Get the subscription pattern
        /// </summary>
        public string Pattern { get { return _pattern; } }

        /// <summary>
        /// Get the message type
        /// </summary>
        public string Type { get { return _type; } }

        internal RedisSubscriptionResponse(string type, string channel, string pattern)
        {
            _type = type;
            _channel = channel;
            _pattern = pattern;
        }
    }

    /// <summary>
    /// Represents a Redis subscription channel
    /// </summary>
    public class RedisSubscriptionChannel : RedisSubscriptionResponse
    {
        readonly long _count;

        /// <summary>
        /// Get the count of active subscriptions
        /// </summary>
        public long Count { get { return _count; } }

        internal RedisSubscriptionChannel(string type, string channel, string pattern, long count)
            : base(type, channel, pattern)
        {
            _count = count;
        }
    }

    /// <summary>
    /// Represents a Redis subscription message
    /// </summary>
    public class RedisSubscriptionMessage : RedisSubscriptionResponse
    {
        readonly string _body;

        /// <summary>
        /// Get the subscription message
        /// </summary>
        public string Body { get { return _body; } }

        internal RedisSubscriptionMessage(string type, string channel, string body)
            : base(type, channel, null)
        {
            _body = body;
        }

        internal RedisSubscriptionMessage(string type, string pattern, string channel, string body)
            : base(type, channel, pattern)
        {
            _body = body;
        }
    }


    /// <summary>
    /// Base class for Redis server-info objects reported by Sentinel
    /// </summary>
    public abstract class RedisServerInfo : ISerializable
    {
        /// <summary>
        /// Create new RedisServerInfo via deserialization
        /// </summary>
        /// <param name="info">Serialization info</param>
        /// <param name="context">Serialization context</param>
        public RedisServerInfo(SerializationInfo info, StreamingContext context)
        {
            Name = info.GetString("name");
            Ip = info.GetString("ip");
            Port = info.GetInt32("port");
            RunId = info.GetString("runid");
            Flags = info.GetString("flags").Split(',');
            PendingCommands = info.GetInt64("pending-commands");
            LastOkPingReply = info.GetInt64("last-ok-ping-reply");
            LastPingReply = info.GetInt64("last-ping-reply");
            DownAfterMilliseconds = info.GetInt64("down-after-milliseconds");
        }

        /// <summary>
        /// Get or set Redis server name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Get or set Redis server IP
        /// </summary>
        public string Ip { get; set; }

        /// <summary>
        /// Get or set Redis server port
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// Get or set Redis server run ID
        /// </summary>
        public string RunId { get; set; }

        /// <summary>
        /// Get or set Redis server flags
        /// </summary>
        public string[] Flags { get; set; }

        /// <summary>
        /// Get or set number of pending Redis server commands
        /// </summary>
        public long PendingCommands { get; set; }

        /// <summary>
        /// Get or set last ping sent
        /// </summary>
        public long LastPingSent { get; set; }

        /// <summary>
        /// Get or set milliseconds since last successful ping reply
        /// </summary>
        public long LastOkPingReply { get; set; }

        /// <summary>
        /// Get or set milliseconds since last ping reply
        /// </summary>
        public long LastPingReply { get; set; }

        /// <summary>
        /// Get or set down after milliseconds
        /// </summary>
        public long DownAfterMilliseconds { get; set; }

        /// <summary>
        /// Not implemented
        /// </summary>
        /// <param name="info">info</param>
        /// <param name="context">info</param>
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Base class for Redis master/slave objects reported by Sentinel
    /// </summary>
    public abstract class RedisMasterSlaveInfo : RedisServerInfo
    {
        /// <summary>
        /// Create new RedisMasterSlaveInfo via deserialization
        /// </summary>
        /// <param name="info">Serialization info</param>
        /// <param name="context">Serialization context</param>
        public RedisMasterSlaveInfo(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            InfoRefresh = info.GetInt64("info-refresh");
            RoleReported = info.GetString("role-reported");
            RoleReportedTime = info.GetInt64("role-reported-time");
        }

        /// <summary>
        /// Get or set info refresh
        /// </summary>
        public long InfoRefresh { get; set; }

        /// <summary>
        /// Get or set role reported
        /// </summary>
        public string RoleReported { get; set; }

        /// <summary>
        /// Get or set role reported time
        /// </summary>
        public long RoleReportedTime { get; set; }
    }

    /// <summary>
    /// Represents a Redis master node as reported by a Redis Sentinel
    /// </summary>
    public class RedisMasterInfo : RedisMasterSlaveInfo
    {
        /// <summary>
        /// Create new RedisMasterInfo via deserialization
        /// </summary>
        /// <param name="info">Serialization info</param>
        /// <param name="context">Serialization context</param>
        public RedisMasterInfo(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            ConfigEpoch = info.GetInt64("config-epoch");
            NumSlaves = info.GetInt64("num-slaves");
            NumOtherSentinels = info.GetInt64("num-other-sentinels");
            Quorum = info.GetInt64("quorum");
            FailoverTimeout = info.GetInt64("failover-timeout");
            ParallelSyncs = info.GetInt64("parallel-syncs");
        }

        /// <summary>
        /// Get or set the config epoch
        /// </summary>
        public long ConfigEpoch { get; set; }
        /// <summary>
        /// Get or set number of slaves of the current master node
        /// </summary>
        public long NumSlaves { get; set; }
        /// <summary>
        /// Get or set number of other Sentinels
        /// </summary>
        public long NumOtherSentinels { get; set; }
        /// <summary>
        /// Get or set Sentinel quorum count
        /// </summary>
        public long Quorum { get; set; }
        /// <summary>
        /// Get or set the failover timeout
        /// </summary>
        public long FailoverTimeout { get; set; }
        /// <summary>
        /// Get or set the parallel syncs
        /// </summary>
        public long ParallelSyncs { get; set; }
    }



    /// <summary>
    /// Represents a Redis slave node as reported by a Redis Setinel
    /// </summary>
    public class RedisSlaveInfo : RedisMasterSlaveInfo
    {
        /// <summary>
        /// Create new RedisSlaveInfo via deserialization
        /// </summary>
        /// <param name="info">Serialization info</param>
        /// <param name="context">Serialization context</param>
        public RedisSlaveInfo(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            MasterLinkDownTime = info.GetInt64("master-link-down-time");
            MasterLinkStatus = info.GetString("master-link-status");
            MasterHost = info.GetString("master-host");
            MasterPort = info.GetInt32("master-port");
            SlavePriority = info.GetInt64("slave-priority");
            SlaveReplOffset = info.GetInt64("slave-repl-offset");
        }

        /// <summary>
        /// Get or set the master link down time
        /// </summary>
        public long MasterLinkDownTime { get; set; }

        /// <summary>
        /// Get or set status of master link
        /// </summary>
        public string MasterLinkStatus { get; set; }

        /// <summary>
        /// Get or set the master host of the current Redis slave node
        /// </summary>
        public string MasterHost { get; set; }

        /// <summary>
        /// Get or set the master port of the current Redis slave node
        /// </summary>
        public int MasterPort { get; set; }

        /// <summary>
        /// Get or set the priority of the current Redis slave node
        /// </summary>
        public long SlavePriority { get; set; }

        /// <summary>
        /// Get or set the slave replication offset
        /// </summary>
        public long SlaveReplOffset { get; set; }
    }

    /// <summary>
    /// Represents a Redis Sentinel node as reported by a Redis Sentinel
    /// </summary>
    public class RedisSentinelInfo : RedisServerInfo
    {
        /// <summary>
        /// Create new RedisSentinelInfo via deserialization
        /// </summary>
        /// <param name="info">Serialization info</param>
        /// <param name="context">Serialization context</param>
        public RedisSentinelInfo(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            SDownTime = info.GetInt64("s-down-time");
            LastHelloMessage = info.GetInt64("last-hello-message");
            VotedLeader = info.GetString("voted-leader");
            VotedLeaderEpoch = info.GetInt64("voted-leader-epoch");
        }

        /// <summary>
        /// Get or set the subjective down time
        /// </summary>
        public long SDownTime { get; set; }

        /// <summary>
        /// Get or set milliseconds(?) since last hello message from current Sentinel node
        /// </summary>
        public long LastHelloMessage { get; set; }

        /// <summary>
        /// Get or set the voted-leader value
        /// </summary>
        public string VotedLeader { get; set; }
        /// <summary>
        /// Get or set the voted-leader epoch
        /// </summary>
        public long VotedLeaderEpoch { get; set; }
    }

    /// <summary>
    /// Represents an entry from the Redis slow log
    /// </summary>
    public class RedisSlowLogEntry
    {
        readonly long _id;
        readonly DateTime _date;
        readonly TimeSpan _latency;
        readonly string[] _arguments;

        /// <summary>
        /// Get the entry ID
        /// </summary>
        public long Id { get { return _id; } }
        /// <summary>
        /// Get the entry date
        /// </summary>
        public DateTime Date { get { return _date; } }
        /// <summary>
        /// Get the entry latency
        /// </summary>
        public TimeSpan Latency { get { return _latency; } }
        /// <summary>
        /// Get the entry arguments
        /// </summary>
        public string[] Arguments { get { return _arguments; } }

        internal RedisSlowLogEntry(long id, DateTime date, TimeSpan latency, string[] arguments)
        {
            _id = id;
            _date = date;
            _latency = latency;
            _arguments = arguments;
        }
    }

    /// <summary>
    /// Represents state as reported by Sentinel
    /// </summary>
    public class RedisMasterState
    {
        readonly long _downState;
        readonly string _leader;
        readonly long _voteEpoch;

        /// <summary>
        /// Get the master down state
        /// </summary>
        public long DownState { get { return _downState; } }
        /// <summary>
        /// Get the leader
        /// </summary>
        public string Leader { get { return _leader; } }
        /// <summary>
        /// Get the vote epoch
        /// </summary>
        public long VoteEpoch { get { return _voteEpoch; } }

        internal RedisMasterState(long downState, string leader, long voteEpoch)
        {
            _downState = downState;
            _leader = leader;
            _voteEpoch = voteEpoch;
        }
    }
}
