using CSRedis.Internal.Commands;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

#if net40
#else
namespace CSRedis
{
    public partial class RedisClient
    {
        /// <summary>
        /// Open connection to redis server
        /// </summary>
        /// <returns>True on success</returns>
        public Task<bool> ConnectAsync()
        {
            return _connector.ConnectAsync();
        }

        /// <summary>
        /// Call arbitrary redis command
        /// </summary>
        /// <param name="command"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        public Task<object> CallAsync(string command, params string[] args)
        {
            return WriteAsync(RedisCommands.Call(command, args));
        }

        internal ConcurrentQueue<TaskCompletionSource<object>> _asyncPipe;
        async Task<T> WriteAsync<T>(RedisCommand<T> command)
        {
            if (_transaction.Active)
                return await  _transaction.WriteAsync(command);
            else if (_asyncPipe != null)
            {
                var tsc = new TaskCompletionSource<object>();
                _asyncPipe.Enqueue(tsc);
                
                _connector.Pipeline.Write(command);

                var ret = await tsc.Task;
                return (T)ret;
            }
            else
                return await _connector.CallAsync(command);
        }

        #region Connection
        /// <summary>
        /// Authenticate to the server
        /// </summary>
        /// <param name="password">Server password</param>
        /// <returns>Task associated with status message</returns>
        public Task<string> AuthAsync(string password)
        {
            return WriteAsync(RedisCommands.Auth(password));
        }

        /// <summary>
        /// Echo the given string
        /// </summary>
        /// <param name="message">Message to echo</param>
        /// <returns>Task associated with echo response</returns>
        public Task<string> EchoAsync(string message)
        {
            return WriteAsync(RedisCommands.Echo(message));
        }

        /// <summary>
        /// Ping the server
        /// </summary>
        /// <returns>Task associated with status message</returns>
        public Task<string> PingAsync()
        {
            return WriteAsync(RedisCommands.Ping());
        }

        /// <summary>
        /// Close the connection
        /// </summary>
        /// <returns>Task associated with status message</returns>
        public Task<string> QuitAsync()
        {
            return WriteAsync(RedisCommands.Quit())
                .ContinueWith<string>(t =>
                {
                    _connector.Dispose();
                    return t.Result;
                });
        }

        /// <summary>
        /// Change the selected database for the current connection
        /// </summary>
        /// <param name="index">Zero-based database index</param>
        /// <returns>Status message</returns>
        public Task<string> SelectAsync(int index)
        {
            return WriteAsync(RedisCommands.Select(index));
        }
        #endregion

        #region Keys
        /// <summary>
        /// [redis-server 3.2.1] 修改指定key(s) 最后访问时间 若key不存在，不做操作
        /// </summary>
        /// <param name="keys">Keys</param>
        /// <returns></returns>
        public Task<long> TouchAsync(params string[] keys)
        {
            return WriteAsync(RedisCommands.Touch(keys));
        }
        /// <summary>
        /// [redis-server 4.0.0] Delete a key, 该命令和DEL十分相似：删除指定的key(s),若key不存在则该key被跳过。但是，相比DEL会产生阻塞，该命令会在另一个线程中回收内存，因此它是非阻塞的。 这也是该命令名字的由来：仅将keys从keyspace元数据中删除，真正的删除会在后续异步操作。
        /// </summary>
        /// <param name="keys">Keys to delete</param>
        /// <returns>Number of keys removed</returns>
        public Task<long> UnLinkAsync(params string[] keys)
        {
            return WriteAsync(RedisCommands.UnLink(keys));
        }
        /// <summary>
        /// Delete a key
        /// </summary>
        /// <param name="keys">Keys to delete</param>
        /// <returns></returns>
        public Task<long> DelAsync(params string[] keys)
        {
            return WriteAsync(RedisCommands.Del(keys));
        }

        /// <summary>
        /// Return a serialized version of the value stored at the specified key
        /// </summary>
        /// <param name="key">Key to dump</param>
        /// <returns></returns>
        public Task<byte[]> DumpAsync(string key)
        {
            return WriteAsync(RedisCommands.Dump(key));
        }

        /// <summary>
        /// Determine if a key exists
        /// </summary>
        /// <param name="key">Key to check</param>
        /// <returns></returns>
        public Task<bool> ExistsAsync(string key)
        {
            return WriteAsync(RedisCommands.Exists(key));
        }
        public Task<long> ExistsAsync(string[] keys)
        {
            return WriteAsync(RedisCommands.Exists(keys));
        }

        /// <summary>
        /// Set a key's time to live in seconds
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="expiration">Expiration (nearest second)</param>
        /// <returns></returns>
        public Task<bool> ExpireAsync(string key, int expiration)
        {
            return WriteAsync(RedisCommands.Expire(key, expiration));
        }

        /// <summary>
        /// Set a key's time to live in seconds
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="expiration">Expiration in seconds</param>
        /// <returns></returns>
        public Task<bool> ExpireAsync(string key, TimeSpan expiration)
        {
            return WriteAsync(RedisCommands.Expire(key, expiration));
        }

        /// <summary>
        /// Set the expiration for a key (nearest second)
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="expirationDate">Date of expiration, to nearest second</param>
        /// <returns></returns>
        public Task<bool> ExpireAtAsync(string key, DateTime expirationDate)
        {
            return WriteAsync(RedisCommands.ExpireAt(key, expirationDate));
        }

        /// <summary>
        /// Set the expiration for a key as a UNIX timestamp
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="timestamp"></param>
        /// <returns></returns>
        public Task<bool> ExpireAtAsync(string key, int timestamp)
        {
            return WriteAsync(RedisCommands.ExpireAt(key, timestamp));
        }

        /// <summary>
        /// Find all keys matching the given pattern
        /// </summary>
        /// <param name="pattern">Pattern to match</param>
        /// <returns></returns>
        public Task<string[]> KeysAsync(string pattern)
        {
            return WriteAsync(RedisCommands.Keys(pattern));
        }

        /// <summary>
        /// Atomically transfer a key from a Redis instance to another one
        /// </summary>
        /// <param name="host">Remote Redis host</param>
        /// <param name="port">Remote Redis port</param>
        /// <param name="key">Key to migrate</param>
        /// <param name="destinationDb">Remote database ID</param>
        /// <param name="timeout">Timeout in milliseconds</param>
        /// <returns></returns>
        public Task<string> MigrateAsync(string host, int port, string key, int destinationDb, int timeout)
        {
            return WriteAsync(RedisCommands.Migrate(host, port, key, destinationDb, timeout));
        }

        /// <summary>
        /// Atomically transfer a key from a Redis instance to another one
        /// </summary>
        /// <param name="host">Remote Redis host</param>
        /// <param name="port">Remote Redis port</param>
        /// <param name="key">Key to migrate</param>
        /// <param name="destinationDb">Remote database ID</param>
        /// <param name="timeout">Timeout in milliseconds</param>
        /// <returns></returns>
        public Task<string> MigrateAsync(string host, int port, string key, int destinationDb, TimeSpan timeout)
        {
            return WriteAsync(RedisCommands.Migrate(host, port, key, destinationDb, timeout));
        }

        /// <summary>
        /// Move a key to another database
        /// </summary>
        /// <param name="key">Key to move</param>
        /// <param name="database">Database destination ID</param>
        /// <returns></returns>
        public Task<bool> MoveAsync(string key, int database)
        {
            return WriteAsync(RedisCommands.Move(key, database));
        }

        /// <summary>
        /// Get the number of references of the value associated with the specified key
        /// </summary>
        /// <param name="arguments">Subcommand arguments</param>
        /// <returns>The type of internal representation used to store the value at the specified key</returns>
        public Task<string> ObjectEncodingAsync(params string[] arguments)
        {
            return WriteAsync(RedisCommands.ObjectEncoding(arguments));
        }

        /// <summary>
        /// Inspect the internals of Redis objects
        /// </summary>
        /// <param name="subCommand">Type of Object command to send</param>
        /// <param name="arguments">Subcommand arguments</param>
        /// <returns>Varies depending on subCommand</returns>
        public Task<long?> ObjectAsync(RedisObjectSubCommand subCommand, params string[] arguments)
        {
            return WriteAsync(RedisCommands.Object(subCommand, arguments));
        }

        /// <summary>
        /// Remove the expiration from a key
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <returns></returns>
        public Task<bool> PersistAsync(string key)
        {
            return WriteAsync(RedisCommands.Persist(key));
        }

        /// <summary>
        /// Set a key's time to live in milliseconds
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="expiration">Expiration (nearest millisecond)</param>
        /// <returns></returns>
        public Task<bool> PExpireAsync(string key, TimeSpan expiration)
        {
            return WriteAsync(RedisCommands.PExpire(key, expiration));
        }

        /// <summary>
        /// Set a key's time to live in milliseconds
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="milliseconds">Expiration in milliseconds</param>
        /// <returns></returns>
        public Task<bool> PExpireAsync(string key, long milliseconds)
        {
            return WriteAsync(RedisCommands.PExpire(key, milliseconds));
        }

        /// <summary>
        /// Set the expiration for a key (nearest millisecond)
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="date">Expiration date</param>
        /// <returns></returns>
        public Task<bool> PExpireAtAsync(string key, DateTime date)
        {
            return WriteAsync(RedisCommands.PExpireAt(key, date));
        }

        /// <summary>
        /// Set the expiration for a key as a UNIX timestamp specified in milliseconds
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="timestamp">Expiration timestamp (milliseconds)</param>
        /// <returns></returns>
        public Task<bool> PExpireAtAsync(string key, long timestamp)
        {
            return WriteAsync(RedisCommands.PExpireAt(key, timestamp));
        }

        /// <summary>
        /// Get the time to live for a key in milliseconds
        /// </summary>
        /// <param name="key">Key to check</param>
        /// <returns></returns>
        public Task<long> PTtlAsync(string key)
        {
            return WriteAsync(RedisCommands.PTtl(key));
        }

        /// <summary>
        /// Return a random key from the keyspace
        /// </summary>
        /// <returns></returns>
        public Task<string> RandomKeyAsync()
        {
            return WriteAsync(RedisCommands.RandomKey());
        }

        /// <summary>
        /// Rename a key
        /// </summary>
        /// <param name="key">Key to rename</param>
        /// <param name="newKey">New key name</param>
        /// <returns></returns>
        public Task<string> RenameAsync(string key, string newKey)
        {
            return WriteAsync(RedisCommands.Rename(key, newKey));
        }

        /// <summary>
        /// Rename a key, only if the new key does not exist
        /// </summary>
        /// <param name="key">Key to rename</param>
        /// <param name="newKey">New key name</param>
        /// <returns></returns>
        public Task<bool> RenameNxAsync(string key, string newKey)
        {
            return WriteAsync(RedisCommands.RenameNx(key, newKey));
        }

        /// <summary>
        /// Create a key using the provided serialized value, previously obtained using dump
        /// </summary>
        /// <param name="key">Key to restore</param>
        /// <param name="ttlMilliseconds">Time-to-live in milliseconds</param>
        /// <param name="serializedValue">Serialized value from DUMP</param>
        /// <returns></returns>
        public Task<string> RestoreAsync(string key, long ttlMilliseconds, byte[] serializedValue)
        {
            return WriteAsync(RedisCommands.Restore(key, ttlMilliseconds, serializedValue));
        }

        /// <summary>
        /// Sort the elements in a list, set or sorted set
        /// </summary>
        /// <param name="key">Key to sort</param>
        /// <param name="offset">Number of elements to skip</param>
        /// <param name="count">Number of elements to return</param>
        /// <param name="by">Sort by external key</param>
        /// <param name="dir">Sort direction</param>
        /// <param name="isAlpha">Sort lexicographically</param>
        /// <param name="get">Retrieve external keys</param>
        /// <returns></returns>
        public Task<string[]> SortAsync(string key, long? offset = null, long? count = null, string by = null, RedisSortDir? dir = null, bool? isAlpha = null, params string[] get)
        {
            return WriteAsync(RedisCommands.Sort(key, offset, count, by, dir, isAlpha, get));
        }

        /// <summary>
        /// Sort the elements in a list, set or sorted set, then store the result in a new list
        /// </summary>
        /// <param name="key">Key to sort</param>
        /// <param name="destination">Destination key name of stored sort</param>
        /// <param name="offset">Number of elements to skip</param>
        /// <param name="count">Number of elements to return</param>
        /// <param name="by">Sort by external key</param>
        /// <param name="dir">Sort direction</param>
        /// <param name="isAlpha">Sort lexicographically</param>
        /// <param name="get">Retrieve external keys</param>
        /// <returns></returns>
        public Task<long> SortAndStoreAsync(string key, string destination, long? offset = null, long? count = null, string by = null, RedisSortDir? dir = null, bool? isAlpha = null, params string[] get)
        {
            return WriteAsync(RedisCommands.SortAndStore(key, destination, offset, count, by, dir, isAlpha, get));
        }

        /// <summary>
        /// Get the time to live for a key
        /// </summary>
        /// <param name="key">Key to check</param>
        /// <returns></returns>
        public Task<long> TtlAsync(string key)
        {
            return WriteAsync(RedisCommands.Ttl(key));
        }

        /// <summary>
        /// Determine the type stored at key
        /// </summary>
        /// <param name="key">Key to check</param>
        /// <returns></returns>
        public Task<string> TypeAsync(string key)
        {
            return WriteAsync(RedisCommands.Type(key));
        }

        /// <summary>
        /// Iterate the set of keys in the currently selected Redis database
        /// </summary>
        /// <param name="cursor">The cursor returned by the server in the previous call, or 0 if this is the first call</param>
        /// <param name="pattern">Glob-style pattern to filter returned elements</param>
        /// <param name="count">Set the maximum number of elements to return</param>
        /// <returns>Updated cursor and result set</returns>
        public Task<RedisScan<string>> ScanAsync(long cursor, string pattern = null, long? count = null)
        {
            return WriteAsync(RedisCommands.Scan(cursor, pattern, count));
        }
        public Task<RedisScan<byte[]>> ScanBytesAsync(long cursor, string pattern = null, long? count = null)
        {
            return WriteAsync(RedisCommands.ScanBytes(cursor, pattern, count));
        }
        #endregion

        #region Hashes
        /// <summary>
        /// [redis-server 3.2.0] 返回hash指定field的value的字符串长度，如果hash或者field不存在，返回0.
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <param name="field">Field</param>
        /// <returns></returns>
        public Task<long> HStrLenAsync(string key, string field)
        {
            return WriteAsync(RedisCommands.HStrLen(key, field));
        }

        /// <summary>
        /// Delete one or more hash fields
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <param name="fields">Fields to delete</param>
        /// <returns>Number of fields removed from hash</returns>
        public Task<long> HDelAsync(string key, params string[] fields)
        {
            return WriteAsync(RedisCommands.HDel(key, fields));
        }

        /// <summary>
        /// Determine if a hash field exists
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <param name="field">Field to check</param>
        /// <returns>True if hash field exists</returns>
        public Task<bool> HExistsAsync(string key, string field)
        {
            return WriteAsync(RedisCommands.HExists(key, field));
        }

        /// <summary>
        /// Get the value of a hash field
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <param name="field">Field to get</param>
        /// <returns>Value of hash field</returns>
        public Task<string> HGetAsync(string key, string field)
        {
            return WriteAsync(RedisCommands.HGet(key, field));
        }
        public Task<byte[]> HGetBytesAsync(string key, string field)
        {
            return WriteAsync(RedisCommands.HGetBytes(key, field));
        }

        /// <summary>
        /// Get all the fields and values in a hash
        /// </summary>
        /// <typeparam name="T">Object to map hash</typeparam>
        /// <param name="key">Hash key</param>
        /// <returns>Strongly typed object mapped from hash</returns>
        public Task<T> HGetAllAsync<T>(string key)
            where T : class
        {
            return WriteAsync(RedisCommands.HGetAll<T>(key));
        }

        /// <summary>
        /// Get all the fields and values in a hash
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <returns>Dictionary mapped from string</returns>
        public Task<Dictionary<string, string>> HGetAllAsync(string key)
        {
            return WriteAsync(RedisCommands.HGetAll(key));
        }
        public Task<Dictionary<string, byte[]>> HGetAllBytesAsync(string key)
        {
            return WriteAsync(RedisCommands.HGetAllBytes(key));
        }

        /// <summary>
        /// Increment the integer value of a hash field by the given number
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <param name="field">Field to increment</param>
        /// <param name="increment">Increment value</param>
        /// <returns>Value of field after increment</returns>
        public Task<long> HIncrByAsync(string key, string field, long increment)
        {
            return WriteAsync(RedisCommands.HIncrBy(key, field, increment));
        }

        /// <summary>
        /// Increment the float value of a hash field by the given number
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <param name="field">Field to increment</param>
        /// <param name="increment">Increment value</param>
        /// <returns>Value of field after increment</returns>
        public Task<decimal> HIncrByFloatAsync(string key, string field, decimal increment)
        {
            return WriteAsync(RedisCommands.HIncrByFloat(key, field, increment));
        }

        /// <summary>
        /// Get all the fields in a hash
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <returns>All hash field names</returns>
        public Task<string[]> HKeysAsync(string key)
        {
            return WriteAsync(RedisCommands.HKeys(key));
        }

        /// <summary>
        /// Get the number of fields in a hash
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <returns>Number of fields in hash</returns>
        public Task<long> HLenAsync(string key)
        {
            return WriteAsync(RedisCommands.HLen(key));
        }

        /// <summary>
        /// Get the values of all the given hash fields
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <param name="fields">Fields to return</param>
        /// <returns>Values of given fields</returns>
        public Task<string[]> HMGetAsync(string key, params string[] fields)
        {
            return WriteAsync(RedisCommands.HMGet(key, fields));
        }
        public Task<byte[][]> HMGetBytesAsync(string key, params string[] fields)
        {
            return WriteAsync(RedisCommands.HMGetBytes(key, fields));
        }

        /// <summary>
        /// Set multiple hash fields to multiple values
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <param name="dict">Dictionary mapping of hash</param>
        /// <returns>Status code</returns>
        public Task<string> HMSetAsync(string key, Dictionary<string, object> dict)
        {
            return WriteAsync(RedisCommands.HMSet(key, dict));
        }

        /// <summary>
        /// Set multiple hash fields to multiple values
        /// </summary>
        /// <typeparam name="T">Type of object to map hash</typeparam>
        /// <param name="key">Hash key</param>
        /// <param name="obj">Object mapping of hash</param>
        /// <returns>Status code</returns>
        public Task<string> HMSetAsync<T>(string key, T obj)
            where T : class
        {
            return WriteAsync(RedisCommands.HMSet<T>(key, obj));
        }

        /// <summary>
        /// Set multiple hash fields to multiple values
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <param name="keyValues">Array of [key,value,key,value,..]</param>
        /// <returns>Status code</returns>
        public Task<string> HMSetAsync(string key, params object[] keyValues)
        {
            return WriteAsync(RedisCommands.HMSet(key, keyValues));
        }

        /// <summary>
        /// Set the value of a hash field
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <param name="field">Hash field to set</param>
        /// <param name="value">Value to set</param>
        /// <returns>True if field is new</returns>
        public Task<bool> HSetAsync(string key, string field, object value)
        {
            return WriteAsync(RedisCommands.HSet(key, field, value));
        }

        /// <summary>
        /// Set the value of a hash field, only if the field does not exist
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <param name="field">Hash field to set</param>
        /// <param name="value">Value to set</param>
        /// <returns>True if field was set to value</returns>
        public Task<bool> HSetNxAsync(string key, string field, object value)
        {
            return WriteAsync(RedisCommands.HSetNx(key, field, value));
        }

        /// <summary>
        /// Get all the values in a hash
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <returns>Array of all values in hash</returns>
        public Task<string[]> HValsAsync(string key)
        {
            return WriteAsync(RedisCommands.HVals(key));
        }
        public Task<byte[][]> HValsBytesAsync(string key)
        {
            return WriteAsync(RedisCommands.HValsBytes(key));
        }

        /// <summary>
        /// Iterate the keys and values of a hash field
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <param name="cursor">The cursor returned by the server in the previous call, or 0 if this is the first call</param>
        /// <param name="pattern">Glob-style pattern to filter returned elements</param>
        /// <param name="count">Maximum number of elements to return</param>
        /// <returns>Updated cursor and result set</returns>
        public Task<RedisScan<Tuple<string, string>>> HScanAsync(string key, long cursor, string pattern = null, long? count = null)
        {
            return WriteAsync(RedisCommands.HScan(key, cursor, pattern, count));
        }
        public Task<RedisScan<Tuple<string, byte[]>>> HScanBytesAsync(string key, long cursor, string pattern = null, long? count = null)
        {
            return WriteAsync(RedisCommands.HScanBytes(key, cursor, pattern, count));
        }
        #endregion

        #region Lists
        /// <summary>
        /// Get an element from a list by its index
        /// </summary>
        /// <param name="key">List key</param>
        /// <param name="index">Zero-based index of item to return</param>
        /// <returns>Element at index</returns>
        public Task<string> LIndexAsync(string key, long index)
        {
            return WriteAsync(RedisCommands.LIndex(key, index));
        }
        public Task<byte[]> LIndexBytesAsync(string key, long index)
        {
            return WriteAsync(RedisCommands.LIndexBytes(key, index));
        }

        /// <summary>
        /// Insert an element before or after another element in a list
        /// </summary>
        /// <param name="key">List key</param>
        /// <param name="insertType">Relative position</param>
        /// <param name="pivot">Relative element</param>
        /// <param name="value">Element to insert</param>
        /// <returns>Length of list after insert or -1 if pivot not found</returns>
        public Task<long> LInsertAsync(string key, RedisInsert insertType, object pivot, object value)
        {
            return WriteAsync(RedisCommands.LInsert(key, insertType, pivot, value));
        }

        /// <summary>
        /// Get the length of a list
        /// </summary>
        /// <param name="key">List key</param>
        /// <returns>Length of list at key</returns>
        public Task<long> LLenAsync(string key)
        {
            return WriteAsync(RedisCommands.LLen(key));
        }

        /// <summary>
        /// Remove and get the first element in a list
        /// </summary>
        /// <param name="key">List key</param>
        /// <returns>First element in list</returns>
        public Task<string> LPopAsync(string key)
        {
            return WriteAsync(RedisCommands.LPop(key));
        }
        public Task<byte[]> LPopBytesAsync(string key)
        {
            return WriteAsync(RedisCommands.LPopBytes(key));
        }

        /// <summary>
        /// Prepend one or multiple values to a list
        /// </summary>
        /// <param name="key">List key</param>
        /// <param name="values">Values to push</param>
        /// <returns>Length of list after push</returns>
        public Task<long> LPushAsync(string key, params object[] values)
        {
            return WriteAsync(RedisCommands.LPush(key, values));
        }

        /// <summary>
        /// Prepend a value to a list, only if the list exists
        /// </summary>
        /// <param name="key">List key</param>
        /// <param name="value">Value to push</param>
        /// <returns>Length of list after push</returns>
        public Task<long> LPushXAsync(string key, object value)
        {
            return WriteAsync(RedisCommands.LPushX(key, value));
        }

        /// <summary>
        /// Get a range of elements from a list
        /// </summary>
        /// <param name="key">List key</param>
        /// <param name="start">Start offset</param>
        /// <param name="stop">Stop offset</param>
        /// <returns>List of elements in range</returns>
        public Task<string[]> LRangeAsync(string key, long start, long stop)
        {
            return WriteAsync(RedisCommands.LRange(key, start, stop));
        }
        public Task<byte[][]> LRangeBytesAsync(string key, long start, long stop)
        {
            return WriteAsync(RedisCommands.LRangeBytes(key, start, stop));
        }

        /// <summary>
        /// Remove elements from a list
        /// </summary>
        /// <param name="key">List key</param>
        /// <param name="count">&gt;0: remove N elements from head to tail; &lt;0: remove N elements from tail to head; =0: remove all elements</param>
        /// <param name="value">Remove elements equal to value</param>
        /// <returns>Number of removed elements</returns>
        public Task<long> LRemAsync(string key, long count, object value)
        {
            return WriteAsync(RedisCommands.LRem(key, count, value));
        }

        /// <summary>
        /// Set the value of an element in a list by its index
        /// </summary>
        /// <param name="key">List key</param>
        /// <param name="index">List index to modify</param>
        /// <param name="value">New element value</param>
        /// <returns>Status code</returns>
        public Task<string> LSetAsync(string key, long index, object value)
        {
            return WriteAsync(RedisCommands.LSet(key, index, value));
        }

        /// <summary>
        /// Trim a list to the specified range
        /// </summary>
        /// <param name="key">List key</param>
        /// <param name="start">Zero-based start index</param>
        /// <param name="stop">Zero-based stop index</param>
        /// <returns>Status code</returns>
        public Task<string> LTrimAsync(string key, long start, long stop)
        {
            return WriteAsync(RedisCommands.LTrim(key, start, stop));
        }

        /// <summary>
        /// Remove and get the last elment in a list
        /// </summary>
        /// <param name="key">List key</param>
        /// <returns>Value of last list element</returns>
        public Task<string> RPopAsync(string key)
        {
            return WriteAsync(RedisCommands.RPop(key));
        }
        public Task<byte[]> RPopBytesAsync(string key)
        {
            return WriteAsync(RedisCommands.RPopBytes(key));
        }

        /// <summary>
        /// Remove the last elment in a list, append it to another list and return it
        /// </summary>
        /// <param name="source">List source key</param>
        /// <param name="destination">Destination key</param>
        /// <returns>Element being popped and pushed</returns>
        public Task<string> RPopLPushAsync(string source, string destination)
        {
            return WriteAsync(RedisCommands.RPopLPush(source, destination));
        }
        public Task<byte[]> RPopBytesLPushAsync(string source, string destination)
        {
            return WriteAsync(RedisCommands.RPopBytesLPush(source, destination));
        }

        /// <summary>
        /// Append one or multiple values to a list
        /// </summary>
        /// <param name="key">List key</param>
        /// <param name="values">Values to push</param>
        /// <returns>Length of list after push</returns>
        public Task<long> RPushAsync(string key, params object[] values)
        {
            return WriteAsync(RedisCommands.RPush(key, values));
        }

        /// <summary>
        /// Append a value to a list, only if the list exists
        /// </summary>
        /// <param name="key">List key</param>
        /// <param name="value">Value to push</param>
        /// <returns>Length of list after push</returns>
        public Task<long> RPushXAsync(string key, object value)
        {
            return WriteAsync(RedisCommands.RPushX(key, value));
        }
        #endregion

        #region Sets
        /// <summary>
        /// Add one or more members to a set
        /// </summary>
        /// <param name="key">Set key</param>
        /// <param name="members">Members to add to set</param>
        /// <returns>Number of elements added to set</returns>
        public Task<long> SAddAsync(string key, params object[] members)
        {
            return WriteAsync(RedisCommands.SAdd(key, members));
        }

        /// <summary>
        /// Get the number of members in a set
        /// </summary>
        /// <param name="key">Set key</param>
        /// <returns>Number of elements in set</returns>
        public Task<long> SCardAsync(string key)
        {
            return WriteAsync(RedisCommands.SCard(key));
        }

        /// <summary>
        /// Subtract multiple sets
        /// </summary>
        /// <param name="keys">Set keys to subtract</param>
        /// <returns>Array of elements in resulting set</returns>
        public Task<string[]> SDiffAsync(params string[] keys)
        {
            return WriteAsync(RedisCommands.SDiff(keys));
        }
        public Task<byte[][]> SDiffBytesAsync(params string[] keys)
        {
            return WriteAsync(RedisCommands.SDiffBytes(keys));
        }

        /// <summary>
        /// Subtract multiple sets and store the resulting set in a key
        /// </summary>
        /// <param name="destination">Destination key</param>
        /// <param name="keys">Set keys to subtract</param>
        /// <returns>Number of elements in the resulting set</returns>
        public Task<long> SDiffStoreAsync(string destination, params string[] keys)
        {
            return WriteAsync(RedisCommands.SDiffStore(destination, keys));
        }

        /// <summary>
        /// Intersect multiple sets
        /// </summary>
        /// <param name="keys">Set keys to intersect</param>
        /// <returns>Array of elements in resulting set</returns>
        public Task<string[]> SInterAsync(params string[] keys)
        {
            return WriteAsync(RedisCommands.SInter(keys));
        }
        public Task<byte[][]> SInterBytesAsync(params string[] keys)
        {
            return WriteAsync(RedisCommands.SInterBytes(keys));
        }

        /// <summary>
        /// Intersect multiple sets and store the resulting set in a key
        /// </summary>
        /// <param name="destination">Destination key</param>
        /// <param name="keys">Set keys to intersect</param>
        /// <returns>Number of elements in resulting set</returns>
        public Task<long> SInterStoreAsync(string destination, params string[] keys)
        {
            return WriteAsync(RedisCommands.SInterStore(destination, keys));
        }

        /// <summary>
        /// Determine if a given value is a member of a set
        /// </summary>
        /// <param name="key">Set key</param>
        /// <param name="member">Member to lookup</param>
        /// <returns>True if member exists in set</returns>
        public Task<bool> SIsMemberAsync(string key, object member)
        {
            return WriteAsync(RedisCommands.SIsMember(key, member));
        }

        /// <summary>
        /// Get all the members in a set
        /// </summary>
        /// <param name="key">Set key</param>
        /// <returns>All elements in the set</returns>
        public Task<string[]> SMembersAsync(string key)
        {
            return WriteAsync(RedisCommands.SMembers(key));
        }
        public Task<byte[][]> SMembersBytesAsync(string key)
        {
            return WriteAsync(RedisCommands.SMembersBytes(key));
        }

        /// <summary>
        /// Move a member from one set to another
        /// </summary>
        /// <param name="source">Source key</param>
        /// <param name="destination">Destination key</param>
        /// <param name="member">Member to move</param>
        /// <returns>True if element was moved</returns>
        public Task<bool> SMoveAsync(string source, string destination, object member)
        {
            return WriteAsync(RedisCommands.SMove(source, destination, member));
        }

        /// <summary>
        /// Remove and return a random member from a set
        /// </summary>
        /// <param name="key">Set key</param>
        /// <returns>The removed element</returns>
        public Task<string> SPopAsync(string key)
        {
            return WriteAsync(RedisCommands.SPop(key));
        }
        public Task<byte[]> SPopBytesAsync(string key)
        {
            return WriteAsync(RedisCommands.SPopBytes(key));
        }

        /// <summary>
        /// Remove and return one or more random members from a set
        /// </summary>
        /// <param name="key">Set key</param>
        /// <param name="count">Number of elements to remove and return</param>
        /// <returns></returns>
        public Task<string[]> SPopAsync(string key, long count)
        {
            return WriteAsync(RedisCommands.SPop(key, count));
        }
        public Task<byte[][]> SPopBytesAsync(string key, long count)
        {
            return WriteAsync(RedisCommands.SPopBytes(key, count));
        }

        /// <summary>
        /// Get a random member from a set
        /// </summary>
        /// <param name="key">Set key</param>
        /// <returns>One random element from set</returns>
        public Task<string> SRandMemberAsync(string key)
        {
            return WriteAsync(RedisCommands.SRandMember(key));
        }
        public Task<byte[]> SRandMemberBytesAsync(string key)
        {
            return WriteAsync(RedisCommands.SRandMemberBytes(key));
        }

        /// <summary>
        /// Get one or more random members from a set
        /// </summary>
        /// <param name="key">Set key</param>
        /// <param name="count">Number of elements to return</param>
        /// <returns>One or more random elements from set</returns>
        public Task<string[]> SRandMembersAsync(string key, long count)
        {
            return WriteAsync(RedisCommands.SRandMembers(key, count));
        }
        public Task<byte[][]> SRandMembersBytesAsync(string key, long count)
        {
            return WriteAsync(RedisCommands.SRandMembersBytes(key, count));
        }

        /// <summary>
        /// Remove one or more members from a set
        /// </summary>
        /// <param name="key">Set key</param>
        /// <param name="members">Set members to remove</param>
        /// <returns>Number of elements removed from set</returns>
        public Task<long> SRemAsync(string key, params object[] members)
        {
            return WriteAsync(RedisCommands.SRem(key, members));
        }

        /// <summary>
        /// Add multiple sets
        /// </summary>
        /// <param name="keys">Set keys to union</param>
        /// <returns>Array of elements in resulting set</returns>
        public Task<string[]> SUnionAsync(params string[] keys)
        {
            return WriteAsync(RedisCommands.SUnion(keys));
        }
        public Task<byte[][]> SUnionBytesAsync(params string[] keys)
        {
            return WriteAsync(RedisCommands.SUnionBytes(keys));
        }

        /// <summary>
        /// Add multiple sets and store the resulting set in a key
        /// </summary>
        /// <param name="destination">Destination key</param>
        /// <param name="keys">Set keys to union</param>
        /// <returns>Number of elements in resulting set</returns>
        public Task<long> SUnionStoreAsync(string destination, params string[] keys)
        {
            return WriteAsync(RedisCommands.SUnionStore(destination, keys));
        }

        /// <summary>
        /// Iterate the elements of a set field
        /// </summary>
        /// <param name="key">Set key</param>
        /// <param name="cursor">The cursor returned by the server in the previous call, or 0 if this is the first call</param>
        /// <param name="pattern">Glob-style pattern to filter returned elements</param>
        /// <param name="count">Maximum number of elements to return</param>
        /// <returns>Updated cursor and result set</returns>
        public Task<RedisScan<string>> SScanAsync(string key, long cursor, string pattern = null, long? count = null)
        {
            return WriteAsync(RedisCommands.SScan(key, cursor, pattern, count));
        }
        public Task<RedisScan<byte[]>> SScanBytesAsync(string key, long cursor, string pattern = null, long? count = null)
        {
            return WriteAsync(RedisCommands.SScanBytes(key, cursor, pattern, count));
        }
        #endregion

        #region Sorted Sets
        public Task<Tuple<string, decimal>[]> ZPopMaxAsync(string key, long count)
        {
            return WriteAsync(RedisCommands.ZPopMax(key, count));
        }
        public Task<Tuple<byte[], decimal>[]> ZPopMaxBytesAsync(string key, long count)
        {
            return WriteAsync(RedisCommands.ZPopMaxBytes(key, count));
        }
        public Task<Tuple<string, decimal>[]> ZPopMinAsync(string key, long count)
        {
            return WriteAsync(RedisCommands.ZPopMin(key, count));
        }
        public Task<Tuple<byte[], decimal>[]> ZPopMinBytesAsync(string key, long count)
        {
            return WriteAsync(RedisCommands.ZPopMinBytes(key, count));
        }


        /// <summary>
        /// Add one or more members to a sorted set, or update its score if it already exists
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="scoreMembers">Array of member scores to add to sorted set</param>
        /// <returns>Number of elements added to the sorted set (not including member updates)</returns>
        public Task<long> ZAddAsync<TScore, TMember>(string key, params Tuple<TScore, TMember>[] scoreMembers)
        {
            return WriteAsync(RedisCommands.ZAdd(key, scoreMembers));
        }

        /// <summary>
        /// Add one or more members to a sorted set, or update its score if it already exists
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="scoreMembers">Array of member scores [s1, m1, s2, m2, ..]</param>
        /// <returns>Number of elements added to the sorted set (not including member updates)</returns>
        public Task<long> ZAddAsync(string key, params object[] scoreMembers)
        {
            return WriteAsync(RedisCommands.ZAdd(key, scoreMembers));
        }

        /// <summary>
        /// Get the number of members in a sorted set
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <returns>Number of elements in the sorted set</returns>
        public Task<long> ZCardAsync(string key)
        {
            return WriteAsync(RedisCommands.ZCard(key));
        }

        /// <summary>
        /// Count the members in a sorted set with scores within the given values
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="min">Minimum score</param>
        /// <param name="max">Maximum score</param>
        /// <param name="exclusiveMin">Minimum score is exclusive</param>
        /// <param name="exclusiveMax">Maximum score is exclusive</param>
        /// <returns>Number of elements in the specified score range</returns>
        public Task<long> ZCountAsync(string key, decimal min, decimal max, bool exclusiveMin = false, bool exclusiveMax = false)
        {
            return WriteAsync(RedisCommands.ZCount(key, min, max, exclusiveMin, exclusiveMax));
        }

        /// <summary>
        /// Count the members in a sorted set with scores within the given values
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="min">Minimum score</param>
        /// <param name="max">Maximum score</param>
        /// <returns>Number of elements in the specified score range</returns>
        public Task<long> ZCountAsync(string key, string min, string max)
        {
            return WriteAsync(RedisCommands.ZCount(key, min, max));
        }

        /// <summary>
        /// Increment the score of a member in a sorted set
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="increment">Increment by value</param>
        /// <param name="member">Sorted set member to increment</param>
        /// <returns>New score of member</returns>
        public Task<decimal> ZIncrByAsync(string key, decimal increment, object member)
        {
            return WriteAsync(RedisCommands.ZIncrBy(key, increment, member));
        }

        /// <summary>
        /// Intersect multiple sorted sets and store the resulting set in a new key
        /// </summary>
        /// <param name="destination">Destination key</param>
        /// <param name="weights">Multiplication factor for each input set</param>
        /// <param name="aggregate">Aggregation function of resulting set</param>
        /// <param name="keys">Sorted set keys to intersect</param>
        /// <returns>Number of elements in the resulting sorted set</returns>
        public Task<long> ZInterStoreAsync(string destination, decimal[] weights = null, RedisAggregate? aggregate = null, params string[] keys)
        {
            return WriteAsync(RedisCommands.ZInterStore(destination, weights, aggregate, keys));
        }

        /// <summary>
        /// Intersect multiple sorted sets and store the resulting set in a new key
        /// </summary>
        /// <param name="destination">Destination key</param>
        /// <param name="keys">Sorted set keys to intersect</param>
        /// <returns>Number of elements in the resulting sorted set</returns>
        public Task<long> ZInterStoreAsync(string destination, params string[] keys)
        {
            return ZInterStoreAsync(destination, null, null, keys);
        }

        /// <summary>
        /// Return a range of members in a sorted set, by index
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="start">Start offset</param>
        /// <param name="stop">Stop offset</param>
        /// <param name="withScores">Include scores in result</param>
        /// <returns>Array of elements in the specified range (with optional scores)</returns>
        public Task<string[]> ZRangeAsync(string key, long start, long stop, bool withScores = false)
        {
            return WriteAsync(RedisCommands.ZRange(key, start, stop, withScores));
        }
        public Task<byte[][]> ZRangeBytesAsync(string key, long start, long stop, bool withScores = false)
        {
            return WriteAsync(RedisCommands.ZRangeBytes(key, start, stop, withScores));
        }

        /// <summary>
        /// Return a range of members in a sorted set, by index, with scores
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="start">Start offset</param>
        /// <param name="stop">Stop offset</param>
        /// <returns>Array of elements in the specified range with scores</returns>
        public Task<Tuple<string, decimal>[]> ZRangeWithScoresAsync(string key, long start, long stop)
        {
            return WriteAsync(RedisCommands.ZRangeWithScores(key, start, stop));
        }
        public Task<Tuple<byte[], decimal>[]> ZRangeBytesWithScoresAsync(string key, long start, long stop)
        {
            return WriteAsync(RedisCommands.ZRangeBytesWithScores(key, start, stop));
        }

        /// <summary>
        /// Return a range of members in a sorted set, by score
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="min">Minimum score</param>
        /// <param name="max">Maximum score</param>
        /// <param name="withScores">Include scores in result</param>
        /// <param name="exclusiveMin">Minimum score is exclusive</param>
        /// <param name="exclusiveMax">Maximum score is exclusive</param>
        /// <param name="offset">Start offset</param>
        /// <param name="count">Number of elements to return</param>
        /// <returns>List of elements in the specified range (with optional scores)</returns>
        public Task<string[]> ZRangeByScoreAsync(string key, decimal min, decimal max, bool withScores = false, bool exclusiveMin = false, bool exclusiveMax = false, long? offset = null, long? count = null)
        {
            return WriteAsync(RedisCommands.ZRangeByScore(key, min, max, withScores, exclusiveMin, exclusiveMax, offset, count));
        }
        public Task<byte[][]> ZRangeBytesByScoreAsync(string key, decimal min, decimal max, bool withScores = false, bool exclusiveMin = false, bool exclusiveMax = false, long? offset = null, long? count = null)
        {
            return WriteAsync(RedisCommands.ZRangeBytesByScore(key, min, max, withScores, exclusiveMin, exclusiveMax, offset, count));
        }

        /// <summary>
        /// Return a range of members in a sorted set, by score
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="min">Minimum score</param>
        /// <param name="max">Maximum score</param>
        /// <param name="withScores">Include scores in result</param>
        /// <param name="offset">Start offset</param>
        /// <param name="count">Number of elements to return</param>
        /// <returns>List of elements in the specified range (with optional scores)</returns>
        public Task<string[]> ZRangeByScoreAsync(string key, string min, string max, bool withScores = false, long? offset = null, long? count = null)
        {
            return WriteAsync(RedisCommands.ZRangeByScore(key, min, max, withScores, offset, count));
        }
        public Task<byte[][]> ZRangeBytesByScoreAsync(string key, string min, string max, bool withScores = false, long? offset = null, long? count = null)
        {
            return WriteAsync(RedisCommands.ZRangeBytesByScore(key, min, max, withScores, offset, count));
        }

        /// <summary>
        /// Return a range of members in a sorted set, by score, with scores
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="min">Minimum score</param>
        /// <param name="max">Maximum score</param>
        /// <param name="exclusiveMin">Minimum score is exclusive</param>
        /// <param name="exclusiveMax">Maximum score is exclusive</param>
        /// <param name="offset">Start offset</param>
        /// <param name="count">Number of elements to return</param>
        /// <returns>List of elements in the specified range (with optional scores)</returns>
        public Task<Tuple<string, decimal>[]> ZRangeByScoreWithScoresAsync(string key, decimal min, decimal max, bool exclusiveMin = false, bool exclusiveMax = false, long? offset = null, long? count = null)
        {
            return WriteAsync(RedisCommands.ZRangeByScoreWithScores(key, min, max, exclusiveMin, exclusiveMax, offset, count));
        }
        public Task<Tuple<byte[], decimal>[]> ZRangeBytesByScoreWithScoresAsync(string key, decimal min, decimal max, bool exclusiveMin = false, bool exclusiveMax = false, long? offset = null, long? count = null)
        {
            return WriteAsync(RedisCommands.ZRangeBytesByScoreWithScores(key, min, max, exclusiveMin, exclusiveMax, offset, count));
        }

        /// <summary>
        /// Return a range of members in a sorted set, by score, with scores
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="min">Minimum score</param>
        /// <param name="max">Maximum score</param>
        /// <param name="offset">Start offset</param>
        /// <param name="count">Number of elements to return</param>
        /// <returns>List of elements in the specified range (with optional scores)</returns>
        public Task<Tuple<string, decimal>[]> ZRangeByScoreWithScoresAsync(string key, string min, string max, long? offset = null, long? count = null)
        {
            return WriteAsync(RedisCommands.ZRangeByScoreWithScores(key, min, max, offset, count));
        }
        public Task<Tuple<byte[], decimal>[]> ZRangeBytesByScoreWithScoresAsync(string key, string min, string max, long? offset = null, long? count = null)
        {
            return WriteAsync(RedisCommands.ZRangeBytesByScoreWithScores(key, min, max, offset, count));
        }

        /// <summary>
        /// Determine the index of a member in a sorted set
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="member">Member to lookup</param>
        /// <returns>Rank of member or null if key does not exist</returns>
        public Task<long?> ZRankAsync(string key, object member)
        {
            return WriteAsync(RedisCommands.ZRank(key, member));
        }

        /// <summary>
        /// Remove one or more members from a sorted set
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="members">Members to remove</param>
        /// <returns>Number of elements removed</returns>
        public Task<long> ZRemAsync(string key, params object[] members)
        {
            return WriteAsync(RedisCommands.ZRem(key, members));
        }

        /// <summary>
        /// Remove all members in a sorted set within the given indexes
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="start">Start offset</param>
        /// <param name="stop">Stop offset</param>
        /// <returns>Number of elements removed</returns>
        public Task<long> ZRemRangeByRankAsync(string key, long start, long stop)
        {
            return WriteAsync(RedisCommands.ZRemRangeByRank(key, start, stop));
        }

        /// <summary>
        /// Remove all members in a sorted set within the given scores
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="min">Minimum score</param>
        /// <param name="max">Maximum score</param>
        /// <param name="exclusiveMin">Minimum score is exclusive</param>
        /// <param name="exclusiveMax">Maximum score is exclusive</param>
        /// <returns>Number of elements removed</returns>
        public Task<long> ZRemRangeByScoreAsync(string key, decimal min, decimal max, bool exclusiveMin = false, bool exclusiveMax = false)
        {
            return WriteAsync(RedisCommands.ZRemRangeByScore(key, min, max, exclusiveMin, exclusiveMax));
        }
        public Task<long> ZRemRangeByScoreAsync(string key, string min, string max)
        {
            return WriteAsync(RedisCommands.ZRemRangeByScore(key, min, max));
        }

        /// <summary>
        /// Return a range of members in a sorted set, by index, with scores ordered from high to low
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="start">Start offset</param>
        /// <param name="stop">Stop offset</param>
        /// <param name="withScores">Include scores in result</param>
        /// <returns>List of elements in the specified range (with optional scores)</returns>
        public Task<string[]> ZRevRangeAsync(string key, long start, long stop, bool withScores = false)
        {
            return WriteAsync(RedisCommands.ZRevRange(key, start, stop, withScores));
        }
        public Task<byte[][]> ZRevRangeBytesAsync(string key, long start, long stop, bool withScores = false)
        {
            return WriteAsync(RedisCommands.ZRevRangeBytes(key, start, stop, withScores));
        }

        /// <summary>
        /// Return a range of members in a sorted set, by index, with scores ordered from high to low
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="start">Start offset</param>
        /// <param name="stop">Stop offset</param>
        /// <returns>List of elements in the specified range (with optional scores)</returns>
        public Task<Tuple<string, decimal>[]> ZRevRangeWithScoresAsync(string key, long start, long stop)
        {
            return WriteAsync(RedisCommands.ZRevRangeWithScores(key, start, stop));
        }
        public Task<Tuple<byte[], decimal>[]> ZRevRangeBytesWithScoresAsync(string key, long start, long stop)
        {
            return WriteAsync(RedisCommands.ZRevRangeBytesWithScores(key, start, stop));
        }

        /// <summary>
        /// Return a range of members in a sorted set, by score, with scores ordered from high to low
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="max">Maximum score</param>
        /// <param name="min">Minimum score</param>
        /// <param name="withScores">Include scores in result</param>
        /// <param name="exclusiveMax">Maximum score is exclusive</param>
        /// <param name="exclusiveMin">Minimum score is exclusive</param>
        /// <param name="offset">Start offset</param>
        /// <param name="count">Number of elements to return</param>
        /// <returns>List of elements in the specified score range (with optional scores)</returns>
        public Task<string[]> ZRevRangeByScoreAsync(string key, decimal max, decimal min, bool withScores = false, bool exclusiveMax = false, bool exclusiveMin = false, long? offset = null, long? count = null)
        {
            return WriteAsync(RedisCommands.ZRevRangeByScore(key, max, min, withScores, exclusiveMax, exclusiveMin, offset, count));
        }
        public Task<byte[][]> ZRevRangeBytesByScoreAsync(string key, decimal max, decimal min, bool withScores = false, bool exclusiveMax = false, bool exclusiveMin = false, long? offset = null, long? count = null)
        {
            return WriteAsync(RedisCommands.ZRevRangeBytesByScore(key, max, min, withScores, exclusiveMax, exclusiveMin, offset, count));
        }

        /// <summary>
        /// Return a range of members in a sorted set, by score, with scores ordered from high to low
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="max">Maximum score</param>
        /// <param name="min">Minimum score</param>
        /// <param name="withScores">Include scores in result</param>
        /// <param name="offset">Start offset</param>
        /// <param name="count">Number of elements to return</param>
        /// <returns>List of elements in the specified score range (with optional scores)</returns>
        public Task<string[]> ZRevRangeByScoreAsync(string key, string max, string min, bool withScores = false, long? offset = null, long? count = null)
        {
            return WriteAsync(RedisCommands.ZRevRangeByScore(key, max, min, withScores, offset, count));
        }
        public Task<byte[][]> ZRevRangeBytesByScoreAsync(string key, string max, string min, bool withScores = false, long? offset = null, long? count = null)
        {
            return WriteAsync(RedisCommands.ZRevRangeBytesByScore(key, max, min, withScores, offset, count));
        }

        /// <summary>
        /// Return a range of members in a sorted set, by score, with scores ordered from high to low
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="max">Maximum score</param>
        /// <param name="min">Minimum score</param>
        /// <param name="exclusiveMax">Maximum score is exclusive</param>
        /// <param name="exclusiveMin">Minimum score is exclusive</param>
        /// <param name="offset">Start offset</param>
        /// <param name="count">Number of elements to return</param>
        /// <returns>List of elements in the specified score range (with optional scores)</returns>
        public Task<Tuple<string, decimal>[]> ZRevRangeByScoreWithScoresAsync(string key, decimal max, decimal min, bool exclusiveMax = false, bool exclusiveMin = false, long? offset = null, long? count = null)
        {
            return WriteAsync(RedisCommands.ZRevRangeByScoreWithScores(key, max, min, exclusiveMax, exclusiveMin, offset, count));
        }
        public Task<Tuple<byte[], decimal>[]> ZRevRangeBytesByScoreWithScoresAsync(string key, decimal max, decimal min, bool exclusiveMax = false, bool exclusiveMin = false, long? offset = null, long? count = null)
        {
            return WriteAsync(RedisCommands.ZRevRangeBytesByScoreWithScores(key, max, min, exclusiveMax, exclusiveMin, offset, count));
        }

        /// <summary>
        /// Return a range of members in a sorted set, by score, with scores ordered from high to low
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="max">Maximum score</param>
        /// <param name="min">Minimum score</param>
        /// <param name="offset">Start offset</param>
        /// <param name="count">Number of elements to return</param>
        /// <returns>List of elements in the specified score range (with optional scores)</returns>
        public Task<Tuple<string, decimal>[]> ZRevRangeByScoreWithScoresAsync(string key, string max, string min, long? offset = null, long? count = null)
        {
            return WriteAsync(RedisCommands.ZRevRangeByScoreWithScores(key, max, min, offset, count));
        }
        public Task<Tuple<byte[], decimal>[]> ZRevRangeBytesByScoreWithScoresAsync(string key, string max, string min, long? offset = null, long? count = null)
        {
            return WriteAsync(RedisCommands.ZRevRangeBytesByScoreWithScores(key, max, min, offset, count));
        }

        /// <summary>
        /// Determine the index of a member in a sorted set, with scores ordered from high to low
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="member">Member to lookup</param>
        /// <returns>Rank of member, or null if member does not exist</returns>
        public Task<long?> ZRevRankAsync(string key, object member)
        {
            return WriteAsync(RedisCommands.ZRevRank(key, member));
        }

        /// <summary>
        /// Get the score associated with the given member in a sorted set
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="member">Member to lookup</param>
        /// <returns>Score of member, or null if member does not exist</returns>
        public Task<decimal?> ZScoreAsync(string key, object member)
        {
            return WriteAsync(RedisCommands.ZScore(key, member));
        }

        /// <summary>
        /// Add multiple sorted sets and store the resulting sorted set in a new key
        /// </summary>
        /// <param name="destination">Destination key</param>
        /// <param name="weights">Multiplication factor for each input set</param>
        /// <param name="aggregate">Aggregation function of resulting set</param>
        /// <param name="keys">Sorted set keys to union</param>
        /// <returns>Number of elements in the resulting sorted set</returns>
        public Task<long> ZUnionStoreAsync(string destination, decimal[] weights = null, RedisAggregate? aggregate = null, params string[] keys)
        {
            return WriteAsync(RedisCommands.ZUnionStore(destination, weights, aggregate, keys));
        }

        /// <summary>
        /// Iterate the scores and elements of a sorted set field
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="cursor">The cursor returned by the server in the previous call, or 0 if this is the first call</param>
        /// <param name="pattern">Glob-style pattern to filter returned elements</param>
        /// <param name="count">Maximum number of elements to return</param>
        /// <returns>Updated cursor and result set</returns>
        public Task<RedisScan<Tuple<string, decimal>>> ZScanAsync(string key, long cursor, string pattern = null, long? count = null)
        {
            return WriteAsync(RedisCommands.ZScan(key, cursor, pattern, count));
        }
        public Task<RedisScan<Tuple<byte[], decimal>>> ZScanBytesAsync(string key, long cursor, string pattern = null, long? count = null)
        {
            return WriteAsync(RedisCommands.ZScanBytes(key, cursor, pattern, count));
        }

        /// <summary>
        /// Retrieve all the elements in a sorted set with a value between min and max
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="min">Lexagraphic start value. Prefix value with '(' to indicate exclusive; '[' to indicate inclusive. Use '-' or '+' to specify infinity.</param>
        /// <param name="max">Lexagraphic stop value. Prefix value with '(' to indicate exclusive; '[' to indicate inclusive. Use '-' or '+' to specify infinity.</param>
        /// <param name="offset">Limit result set by offset</param>
        /// <param name="count">Limimt result set by size</param>
        /// <returns>List of elements in the specified range</returns>
        public Task<string[]> ZRangeByLexAsync(string key, string min, string max, long? offset = null, long? count = null)
        {
            return WriteAsync(RedisCommands.ZRangeByLex(key, min, max, offset, count));
        }
        public Task<byte[][]> ZRangeBytesByLexAsync(string key, string min, string max, long? offset = null, long? count = null)
        {
            return WriteAsync(RedisCommands.ZRangeBytesByLex(key, min, max, offset, count));
        }

        /// <summary>
        /// Remove all elements in the sorted set with a value between min and max
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="min">Lexagraphic start value. Prefix value with '(' to indicate exclusive; '[' to indicate inclusive. Use '-' or '+' to specify infinity.</param>
        /// <param name="max">Lexagraphic stop value. Prefix value with '(' to indicate exclusive; '[' to indicate inclusive. Use '-' or '+' to specify infinity.</param>
        /// <returns>Number of elements removed</returns>
        public Task<long> ZRemRangeByLexAsync(string key, string min, string max)
        {
            return WriteAsync(RedisCommands.ZRemRangeByLex(key, min, max));
        }

        /// <summary>
        /// Returns the number of elements in the sorted set with a value between min and max.
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="min">Lexagraphic start value. Prefix value with '(' to indicate exclusive; '[' to indicate inclusive. Use '-' or '+' to specify infinity.</param>
        /// <param name="max">Lexagraphic stop value. Prefix value with '(' to indicate exclusive; '[' to indicate inclusive. Use '-' or '+' to specify infinity.</param>
        /// <returns>Number of elements in the specified score range</returns>
        public Task<long> ZLexCountAsync(string key, string min, string max)
        {
            return WriteAsync(RedisCommands.ZLexCount(key, min, max));
        }
        #endregion

        #region Pub/Sub
        /// <summary>
        /// Post a message to a channel
        /// </summary>
        /// <param name="channel">Channel to post message</param>
        /// <param name="message">Message to send</param>
        /// <returns>Number of clients that received the message</returns>
        public Task<long> PublishAsync(string channel, string message)
        {
            return WriteAsync(RedisCommands.Publish(channel, message));
        }

        /// <summary>
        /// List the currently active channels
        /// </summary>
        /// <param name="pattern">Glob-style channel pattern</param>
        /// <returns>Active channel names</returns>
        public Task<string[]> PubSubChannelsAsync(string pattern = null)
        {
            return WriteAsync(RedisCommands.PubSubChannels(pattern));
        }

        /// <summary>
        /// Return the number of subscribers (not counting clients subscribed to patterns) for the specified channels
        /// </summary>
        /// <param name="channels">Channels to query</param>
        /// <returns>Channel names and counts</returns>
        public Task<Tuple<string, long>[]> PubSubNumSubAsync(params string[] channels)
        {
            return WriteAsync(RedisCommands.PubSubNumSub(channels));
        }

        /// <summary>
        /// Return the number of subscriptions to patterns
        /// </summary>
        /// <returns>The number of patterns all the clients are subscribed to</returns>
        public Task<long> PubSubNumPatAsync()
        {
            return WriteAsync(RedisCommands.PubSubNumPat());
        }
        #endregion

        #region Scripting
        /// <summary>
        /// Execute a Lua script server side
        /// </summary>
        /// <param name="script">Script to run on server</param>
        /// <param name="keys">Keys used by script</param>
        /// <param name="arguments">Arguments to pass to script</param>
        /// <returns>Redis object</returns>
        public Task<object> EvalAsync(string script, string[] keys, params object[] arguments)
        {
            return WriteAsync(RedisCommands.Eval(script, keys, arguments));
        }

        /// <summary>
        /// Execute a Lua script server side, sending only the script's cached SHA hash
        /// </summary>
        /// <param name="sha1">SHA1 hash of script</param>
        /// <param name="keys">Keys used by script</param>
        /// <param name="arguments">Arguments to pass to script</param>
        /// <returns>Redis object</returns>
        public Task<object> EvalSHAAsync(string sha1, string[] keys, params object[] arguments)
        {
            return WriteAsync(RedisCommands.EvalSHA(sha1, keys, arguments));
        }

        /// <summary>
        /// Check existence of script SHA hashes in the script cache
        /// </summary>
        /// <param name="sha1s">SHA1 script hashes</param>
        /// <returns>Array of boolean values indicating script existence on server</returns>
        public Task<bool[]> ScriptExistsAsync(params string[] sha1s)
        {
            return WriteAsync(RedisCommands.ScriptExists(sha1s));
        }

        /// <summary>
        /// Remove all scripts from the script cache
        /// </summary>
        /// <returns>Status code</returns>
        public Task<string> ScriptFlushAsync()
        {
            return WriteAsync(RedisCommands.ScriptFlush());
        }

        /// <summary>
        /// Kill the script currently in execution
        /// </summary>
        /// <returns>Status code</returns>
        public Task<string> ScriptKillAsync()
        {
            return WriteAsync(RedisCommands.ScriptKill());
        }

        /// <summary>
        /// Load the specified Lua script into the script cache
        /// </summary>
        /// <param name="script">Lua script to load</param>
        /// <returns>SHA1 hash of script</returns>
        public Task<string> ScriptLoadAsync(string script)
        {
            return WriteAsync(RedisCommands.ScriptLoad(script));
        }
        #endregion

        #region Strings
        /// <summary>
        /// Append a value to a key
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="value">Value to append to key</param>
        /// <returns>Length of string after append</returns>
        public Task<long> AppendAsync(string key, object value)
        {
            return WriteAsync(RedisCommands.Append(key, value));
        }

        /// <summary>
        /// Count set bits in a string
        /// </summary>
        /// <param name="key">Key to check</param>
        /// <param name="start">Start offset</param>
        /// <param name="end">Stop offset</param>
        /// <returns>Number of bits set to 1</returns>
        public Task<long> BitCountAsync(string key, long? start = null, long? end = null)
        {
            return WriteAsync(RedisCommands.BitCount(key, start, end));
        }

        /// <summary>
        /// Perform bitwise operations between strings
        /// </summary>
        /// <param name="operation">Bit command to execute</param>
        /// <param name="destKey">Store result in destination key</param>
        /// <param name="keys">Keys to operate</param>
        /// <returns>Size of string stored in the destination key</returns>
        public Task<long> BitOpAsync(RedisBitOp operation, string destKey, params string[] keys)
        {
            return WriteAsync(RedisCommands.BitOp(operation, destKey, keys));
        }

        /// <summary>
        /// Find first bit set or clear in a string
        /// </summary>
        /// <param name="key">Key to examine</param>
        /// <param name="bit">Bit value (1 or 0)</param>
        /// <param name="start">Examine string at specified byte offset</param>
        /// <param name="end">Examine string to specified byte offset</param>
        /// <returns>Position of the first bit set to the specified value</returns>
        public Task<long> BitPosAsync(string key, bool bit, long? start = null, long? end = null)
        {
            return WriteAsync(RedisCommands.BitPos(key, bit, start, end));
        }

        /// <summary>
        /// Decrement the integer value of a key by one
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <returns>Value of key after decrement</returns>
        public Task<long> DecrAsync(string key)
        {
            return WriteAsync(RedisCommands.Decr(key));
        }

        /// <summary>
        /// Decrement the integer value of a key by the given number
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="decrement">Decrement value</param>
        /// <returns>Value of key after decrement</returns>
        public Task<long> DecrByAsync(string key, long decrement)
        {
            return WriteAsync(RedisCommands.DecrBy(key, decrement));
        }

        /// <summary>
        /// Get the value of a key
        /// </summary>
        /// <param name="key">Key to lookup</param>
        /// <returns>Value of key</returns>
        public Task<string> GetAsync(string key)
        {
            return WriteAsync(RedisCommands.Get(key));
        }
        public Task<byte[]> GetBytesAsync(string key)
        {
            return WriteAsync(RedisCommands.GetBytes(key));
        }

        /// <summary>
        /// Returns the bit value at offset in the string value stored at key
        /// </summary>
        /// <param name="key">Key to lookup</param>
        /// <param name="offset">Offset of key to check</param>
        /// <returns>Bit value stored at offset</returns>
        public Task<bool> GetBitAsync(string key, uint offset)
        {
            return WriteAsync(RedisCommands.GetBit(key, offset));
        }

        /// <summary>
        /// Get a substring of the string stored at a key
        /// </summary>
        /// <param name="key">Key to lookup</param>
        /// <param name="start">Start offset</param>
        /// <param name="end">End offset</param>
        /// <returns>Substring in the specified range</returns>
        public Task<string> GetRangeAsync(string key, long start, long end)
        {
            return WriteAsync(RedisCommands.GetRange(key, start, end));
        }
        public Task<byte[]> GetRangeBytesAsync(string key, long start, long end)
        {
            return WriteAsync(RedisCommands.GetRangeBytes(key, start, end));
        }

        /// <summary>
        /// Set the string value of a key and return its old value
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="value">Value to set</param>
        /// <returns>Old value stored at key, or null if key did not exist</returns>
        public Task<string> GetSetAsync(string key, object value)
        {
            return WriteAsync(RedisCommands.GetSet(key, value));
        }
        public Task<byte[]> GetSetBytesAsync(string key, object value)
        {
            return WriteAsync(RedisCommands.GetSetBytes(key, value));
        }

        /// <summary>
        /// Increment the integer value of a key by one
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <returns>Value of key after increment</returns>
        public Task<long> IncrAsync(string key)
        {
            return WriteAsync(RedisCommands.Incr(key));
        }

        /// <summary>
        /// Increment the integer value of a key by the given amount
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="increment">Increment amount</param>
        /// <returns>Value of key after increment</returns>
        public Task<long> IncrByAsync(string key, long increment)
        {
            return WriteAsync(RedisCommands.IncrBy(key, increment));
        }

        /// <summary>
        /// Increment the float value of a key by the given amount
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="increment">Increment amount</param>
        /// <returns>Value of key after increment</returns>
        public Task<decimal> IncrByFloatAsync(string key, decimal increment)
        {
            return WriteAsync(RedisCommands.IncrByFloat(key, increment));
        }

        /// <summary>
        /// Get the values of all the given keys
        /// </summary>
        /// <param name="keys">Keys to lookup</param>
        /// <returns>Array of values at the specified keys</returns>
        public Task<string[]> MGetAsync(params string[] keys)
        {
            return WriteAsync(RedisCommands.MGet(keys));
        }
        public Task<byte[][]> MGetBytesAsync(params string[] keys)
        {
            return WriteAsync(RedisCommands.MGetBytes(keys));
        }

        /// <summary>
        /// Set multiple keys to multiple values
        /// </summary>
        /// <param name="keyValues">Key values to set</param>
        /// <returns>Status code</returns>
        public Task<string> MSetAsync(params Tuple<string, object>[] keyValues)
        {
            return WriteAsync(RedisCommands.MSet(keyValues));
        }

        /// <summary>
        /// Set multiple keys to multiple values
        /// </summary>
        /// <param name="keyValues">Key values to set [k1, v1, k2, v2, ..]</param>
        /// <returns>Status code</returns>
        public Task<string> MSetAsync(params object[] keyValues)
        {
            return WriteAsync(RedisCommands.MSet(keyValues));
        }

        /// <summary>
        /// Set multiple keys to multiple values, only if none of the keys exist
        /// </summary>
        /// <param name="keyValues">Key values to set</param>
        /// <returns>True if all keys were set</returns>
        public Task<bool> MSetNxAsync(params Tuple<string, object>[] keyValues)
        {
            return WriteAsync(RedisCommands.MSetNx(keyValues));
        }

        /// <summary>
        /// Set multiple keys to multiple values, only if none of the keys exist
        /// </summary>
        /// <param name="keyValues">Key values to set [k1, v1, k2, v2, ..]</param>
        /// <returns>True if all keys were set</returns>
        public Task<bool> MSetNxAsync(params object[] keyValues)
        {
            return WriteAsync(RedisCommands.MSetNx(keyValues));
        }

        /// <summary>
        /// Set the value and expiration in milliseconds of a key
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="milliseconds">Expiration in milliseconds</param>
        /// <param name="value">Value to set</param>
        /// <returns>Status code</returns>
        public Task<string> PSetExAsync(string key, long milliseconds, object value)
        {
            return WriteAsync(RedisCommands.PSetEx(key, milliseconds, value));
        }

        /// <summary>
        /// Set the string value of a key
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="value">Value to set</param>
        /// <returns>Status code</returns>
        public Task<string> SetAsync(string key, object value)
        {
            return WriteAsync(RedisCommands.Set(key, value));
        }

        /// <summary>
        /// Set the string value of a key with atomic expiration and existence condition
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="value">Value to set</param>
        /// <param name="expiration">Set expiration to nearest millisecond</param>
        /// <param name="condition">Set key if existence condition</param>
        /// <returns>Status code, or null if condition not met</returns>
        public Task<string> SetAsync(string key, object value, TimeSpan expiration, RedisExistence? condition = null)
        {
            return WriteAsync(RedisCommands.Set(key, value, expiration, condition));
        }

        /// <summary>
        /// Set the string value of a key with atomic expiration and existence condition
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="value">Value to set</param>
        /// <param name="expirationSeconds">Set expiration to nearest second</param>
        /// <param name="condition">Set key if existence condition</param>
        /// <returns>Status code, or null if condition not met</returns>
        public Task<string> SetAsync(string key, object value, int? expirationSeconds = null, RedisExistence? condition = null)
        {
            return WriteAsync(RedisCommands.Set(key, value, expirationSeconds, condition));
        }

        /// <summary>
        /// Set the string value of a key with atomic expiration and existence condition
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="value">Value to set</param>
        /// <param name="expirationMilliseconds">Set expiration to nearest millisecond</param>
        /// <param name="condition">Set key if existence condition</param>
        /// <returns>Status code, or null if condition not met</returns>
        public Task<string> SetAsync(string key, object value, long? expirationMilliseconds = null, RedisExistence? condition = null)
        {
            return WriteAsync(RedisCommands.Set(key, value, expirationMilliseconds, condition));
        }

        /// <summary>
        /// Sets or clears the bit at offset in the string value stored at key
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="offset">Modify key at offset</param>
        /// <param name="value">Value to set (on or off)</param>
        /// <returns>Original bit stored at offset</returns>
        public Task<bool> SetBitAsync(string key, uint offset, bool value)
        {
            return WriteAsync(RedisCommands.SetBit(key, offset, value));
        }

        /// <summary>
        /// Set the value and expiration of a key
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="seconds">Expiration in seconds</param>
        /// <param name="value">Value to set</param>
        /// <returns>Status code</returns>
        public Task<string> SetExAsync(string key, long seconds, object value)
        {
            return WriteAsync(RedisCommands.SetEx(key, seconds, value));
        }

        /// <summary>
        /// Set the value of a key, only if the key does not exist
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="value">Value to set</param>
        /// <returns>True if key was set</returns>
        public Task<bool> SetNxAsync(string key, object value)
        {
            return WriteAsync(RedisCommands.SetNx(key, value));
        }

        /// <summary>
        /// Overwrite part of a string at key starting at the specified offset
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="offset">Start offset</param>
        /// <param name="value">Value to write at offset</param>
        /// <returns>Length of string after operation</returns>
        public Task<long> SetRangeAsync(string key, uint offset, object value)
        {
            return WriteAsync(RedisCommands.SetRange(key, offset, value));
        }

        /// <summary>
        /// Get the length of the value stored in a key
        /// </summary>
        /// <param name="key">Key to lookup</param>
        /// <returns>Length of string at key</returns>
        public Task<long> StrLenAsync(string key)
        {
            return WriteAsync(RedisCommands.StrLen(key));
        }
        #endregion

        #region Server
        /// <summary>
        /// Asyncronously rewrite the append-only file
        /// </summary>
        /// <returns>Status code</returns>
        public Task<string> BgRewriteAofAsync()
        {
            return WriteAsync(RedisCommands.BgRewriteAof());
        }

        /// <summary>
        /// Asynchronously save the dataset to disk
        /// </summary>
        /// <returns>Status code</returns>
        public Task<string> BgSaveAsync()
        {
            return WriteAsync(RedisCommands.BgSave());
        }

        /// <summary>
        /// Get the current connection name
        /// </summary>
        /// <returns>Connection name</returns>
        public Task<string> ClientGetNameAsync()
        {
            return WriteAsync(RedisCommands.ClientGetName());
        }

        /// <summary>
        /// Kill the connection of a client
        /// </summary>
        /// <param name="ip">Client IP returned from CLIENT LIST</param>
        /// <param name="port">Client port returned from CLIENT LIST</param>
        /// <returns>Status code</returns>
        public Task<string> ClientKillAsync(string ip, int port)
        {
            return WriteAsync(RedisCommands.ClientKill(ip, port));
        }

        /// <summary>
        /// Kill the connection of a client
        /// </summary>
        /// <param name="addr">Client address</param>
        /// <param name="id">Client ID</param>
        /// <param name="type">Client type</param>
        /// <param name="skipMe">Set to true to skip calling client</param>
        /// <returns>The number of clients killed</returns>
        public Task<long> ClientKillAsync(string addr = null, string id = null, string type = null, bool? skipMe = null)
        {
            return WriteAsync(RedisCommands.ClientKill(addr, id, type, skipMe));
        }

        /// <summary>
        /// Get the list of client connections
        /// </summary>
        /// <returns>Formatted string of clients</returns>
        public Task<string> ClientListAsync()
        {
            return WriteAsync(RedisCommands.ClientList());
        }

        /// <summary>
        /// Suspend all the Redis clients for the specified amount of time 
        /// </summary>
        /// <param name="milliseconds">Time in milliseconds to suspend</param>
        /// <returns>Status code</returns>
        public Task<string> ClientPauseAsync(int milliseconds)
        {
            return WriteAsync(RedisCommands.ClientPause(milliseconds));
        }

        /// <summary>
        /// Suspend all the Redis clients for the specified amount of time 
        /// </summary>
        /// <param name="timeout">Time to suspend</param>
        /// <returns>Status code</returns>
        public Task<string> ClientPauseAsync(TimeSpan timeout)
        {
            return WriteAsync(RedisCommands.ClientPause(timeout));
        }

        /// <summary>
        /// Set the current connection name
        /// </summary>
        /// <param name="connectionName">Name of connection (no spaces)</param>
        /// <returns>Status code</returns>
        public Task<string> ClientSetNameAsync(string connectionName)
        {
            return WriteAsync(RedisCommands.ClientSetName(connectionName));
        }

        /// <summary>
        /// Get the value of a configuration paramter
        /// </summary>
        /// <param name="parameter">Configuration parameter to lookup</param>
        /// <returns>Configuration value</returns>
        public Task<Tuple<string, string>[]> ConfigGetAsync(string parameter)
        {
            return WriteAsync(RedisCommands.ConfigGet(parameter));
        }

        /// <summary>
        /// Reset the stats returned by INFO
        /// </summary>
        /// <returns>Status code</returns>
        public Task<string> ConfigResetStatAsync()
        {
            return WriteAsync(RedisCommands.ConfigResetStat());
        }

        /// <summary>
        /// Rewrites the redis.conf file
        /// </summary>
        /// <returns>Status code</returns>
        public Task<string> ConfigRewriteAsync()
        {
            return WriteAsync(RedisCommands.ConfigRewrite());
        }

        /// <summary>
        /// Set a configuration parameter to the given value
        /// </summary>
        /// <param name="parameter">Parameter to set</param>
        /// <param name="value">Value to set</param>
        /// <returns>Status code</returns>
        public Task<string> ConfigSetAsync(string parameter, string value)
        {
            return WriteAsync(RedisCommands.ConfigSet(parameter, value));
        }

        /// <summary>
        /// Return the number of keys in the selected database
        /// </summary>
        /// <returns>Number of keys</returns>
        public Task<long> DbSizeAsync()
        {
            return WriteAsync(RedisCommands.DbSize());
        }

        /// <summary>
        /// Make the server crash :(
        /// </summary>
        /// <returns>Status code</returns>
        public Task<string> DebugSegFaultAsync()
        {
            return WriteAsync(RedisCommands.DebugSegFault());
        }

        /// <summary>
        /// Remove all keys from all databases
        /// </summary>
        /// <returns>Status code</returns>
        public Task<string> FlushAllAsync()
        {
            return WriteAsync(RedisCommands.FlushAll());
        }

        /// <summary>
        /// Remove all keys from the current database
        /// </summary>
        /// <returns>Status code</returns>
        public Task<string> FlushDbAsync()
        {
            return WriteAsync(RedisCommands.FlushDb());
        }

        /// <summary>
        /// Get information and statistics about the server
        /// </summary>
        /// <param name="section">all|default|server|clients|memory|persistence|stats|replication|cpu|commandstats|cluster|keyspace</param>
        /// <returns>Formatted string</returns>
        public Task<string> InfoAsync(string section = null)
        {
            return WriteAsync(RedisCommands.Info());
        }

        /// <summary>
        /// Get the timestamp of the last successful save to disk
        /// </summary>
        /// <returns>Date of last save</returns>
        public Task<DateTime> LastSaveAsync()
        {
            return WriteAsync(RedisCommands.LastSave());
        }

        /// <summary>
        /// Provide information on the role of a Redis instance in the context of replication
        /// </summary>
        /// <returns>Role information</returns>
        public Task<RedisRole> RoleAsync()
        {
            return WriteAsync(RedisCommands.Role());
        }

        /// <summary>
        /// Syncronously save the dataset to disk
        /// </summary>
        /// <returns>Status code</returns>
        public Task<string> SaveAsync()
        {
            return WriteAsync(RedisCommands.Save());
        }

        /// <summary>
        /// Syncronously save the dataset to disk an then shut down the server
        /// </summary>
        /// <param name="save">Force a DB saving operation even if no save points are configured</param>
        /// <returns>Status code</returns>
        public Task<string> ShutdownAsync(bool? save = null)
        {
            return WriteAsync(RedisCommands.Shutdown());
        }

        /// <summary>
        /// Make the server a slave of another instance or promote it as master
        /// </summary>
        /// <param name="host">Master host</param>
        /// <param name="port">master port</param>
        /// <returns>Status code</returns>
        public Task<string> SlaveOfAsync(string host, int port)
        {
            return WriteAsync(RedisCommands.SlaveOf(host, port));
        }

        /// <summary>
        /// Turn off replication, turning the Redis server into a master
        /// </summary>
        /// <returns>Status code</returns>
        public Task<string> SlaveOfNoOneAsync()
        {
            return WriteAsync(RedisCommands.SlaveOfNoOne());
        }

        /// <summary>
        /// Get latest entries from the slow log
        /// </summary>
        /// <param name="count">Limit entries returned</param>
        /// <returns>Slow log entries</returns>
        public Task<RedisSlowLogEntry[]> SlowLogGetAsync(long? count = null)
        {
            return WriteAsync(RedisCommands.SlowLogGet(count));
        }

        /// <summary>
        /// Get the length of the slow log
        /// </summary>
        /// <returns>Slow log length</returns>
        public Task<long> SlowLogLenAsync()
        {
            return WriteAsync(RedisCommands.SlowLogLen());
        }

        /// <summary>
        /// Reset the slow log
        /// </summary>
        /// <returns>Status code</returns>
        public Task<string> SlowLogResetAsync()
        {
            return WriteAsync(RedisCommands.SlowLogReset());
        }

        /// <summary>
        /// Internal command used for replication
        /// </summary>
        /// <returns>Byte array of Redis sync data</returns>
        public Task<byte[]> SyncAsync()
        {
            return WriteAsync(RedisCommands.Sync());
        }

        /// <summary>
        /// Return the current server time
        /// </summary>
        /// <returns>Server time</returns>
        public Task<DateTime> TimeAsync()
        {
            return WriteAsync(RedisCommands.Time());
        }
        #endregion

        #region Transactions
        /// <summary>
        /// Mark the start of a transaction block
        /// </summary>
        /// <returns>Status code</returns>
        public Task<string> MultiAsync()
        {
            return _transaction.StartAsync();
        }

        /// <summary>
        /// Discard all commands issued after MULTI
        /// </summary>
        /// <returns>Status code</returns>
        public Task<string> DiscardAsync()
        {
            return _transaction.AbortAsync();
        }

        /// <summary>
        /// Execute all commands issued after MULTI
        /// </summary>
        /// <returns>Array of output from all transaction commands</returns>
        public Task<object[]> ExecAsync()
        {
            return _transaction.ExecuteAsync();
        }

        /// <summary>
        /// Forget about all watched keys
        /// </summary>
        /// <returns>Status code</returns>
        public Task<string> UnwatchAsync()
        {
            return WriteAsync(RedisCommands.Unwatch());
        }

        /// <summary>
        /// Watch the given keys to determine execution of the MULTI/EXEC block
        /// </summary>
        /// <param name="keys">Keys to watch</param>
        /// <returns>Status code</returns>
        public Task<string> WatchAsync(params string[] keys)
        {
            return WriteAsync(RedisCommands.Watch(keys));
        }
        #endregion

        #region HyperLogLog
        /// <summary>
        /// Adds the specified elements to the specified HyperLogLog.
        /// </summary>
        /// <param name="key">Key to update</param>
        /// <param name="elements">Elements to add</param>
        /// <returns>1 if at least 1 HyperLogLog internal register was altered. 0 otherwise.</returns>
        public Task<bool> PfAddAsync(string key, params object[] elements)
        {
            return WriteAsync(RedisCommands.PfAdd(key, elements));
        }
        /// <summary>
        /// Return the approximated cardinality of the set(s) observed by the HyperLogLog at key(s)
        /// </summary>
        /// <param name="keys">One or more HyperLogLog keys to examine</param>
        /// <returns>Approximated number of unique elements observed via PFADD</returns>
        public Task<long> PfCountAsync(params string[] keys)
        {
            return WriteAsync(RedisCommands.PfCount(keys));
        }
        /// <summary>
        /// Merge N different HyperLogLogs into a single key.
        /// </summary>
        /// <param name="destKey">Where to store the merged HyperLogLogs</param>
        /// <param name="sourceKeys">The HyperLogLogs keys that will be combined</param>
        /// <returns>Status code</returns>
        public Task<string> PfMergeAsync(string destKey, params string[] sourceKeys)
        {
            return WriteAsync(RedisCommands.PfMerge(destKey, sourceKeys));
        }
        #endregion

        #region Geo redis-server 3.2
        public Task<long> GeoAddAsync(string key, params (decimal longitude, decimal latitude, object member)[] values)
        {
            if (values == null || values.Length == 0) throw new Exception("values 参数不能为空");
            var args = new List<object>();
            args.Add(key);
            foreach (var v in values) args.AddRange(new object[] { v.longitude, v.latitude, v.member });
            return WriteAsync(new RedisInt("GEOADD", args.ToArray()));
        }
        public Task<decimal?> GeoDistAsync(string key, object member1, object member2, GeoUnit unit = GeoUnit.m)
        {
            if (unit == GeoUnit.m) return WriteAsync(new RedisFloat.Nullable("GEODIST", key, member1, member2));
            return WriteAsync(new RedisFloat.Nullable("GEODIST", key, member1, member2, unit));
        }
        public Task<string[]> GeoHashAsync(string key, object[] members)
        {
            if (members == null || members.Length == 0) throw new Exception("values 参数不能为空");
            var args = new List<object>();
            args.Add(key);
            args.AddRange(members);
            return WriteAsync(new RedisArray.Strings("GEOHASH", args.ToArray()));
        }
        async public Task<(decimal longitude, decimal latitude)?[]> GeoPosAsync(string key, object[] members)
        {
            if (members == null || members.Length == 0) throw new Exception("values 参数不能为空");
            var args = new List<object>();
            args.Add(key);
            args.AddRange(members);
            var ret = await WriteAsync(new RedisArray.Generic<decimal[]>(new RedisArray.Generic<decimal>(new RedisFloat("GEOPOS", args.ToArray()))));
            return ret.Select(a => a != null && a.Length == 2 ? new (decimal, decimal)?((a[0], a[1])) : null).ToArray();
        }
        async public Task<(string member, decimal dist, decimal longitude, decimal latitude, long hash)[]> GeoRadiusAsync(string key, decimal longitude, decimal latitude, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null, bool withCoord = false, bool withDist = false, bool withHash = false)
        {
            var args = new List<object>(new object[] { key, longitude, latitude, radius, unit });
            if (withCoord) args.Add("WITHCOORD");
            if (withDist) args.Add("WITHDIST");
            if (withHash) args.Add("WITHHASH");
            if (count.HasValue) args.Add(count);
            if (sorting.HasValue) args.Add(sorting);

            var cmd = new RedisTuple.Generic<string, decimal, long, decimal[]>.Single(
                new RedisString(null),
                withDist == false ? null : new RedisFloat(null),
                withHash == false ? null : new RedisInt(null),
                withCoord == false ? null : new RedisArray.Generic<decimal>(new RedisFloat(null)), "GEORADIUS", args.ToArray());
            var ret = await WriteAsync(new RedisArray.Generic<Tuple<string, decimal, long, decimal[]>>(cmd));
            return ret.Select(a => (a.Item1, a.Item2, a.Item4 == null ? default(decimal) : a.Item4[0], a.Item4 == null ? default(decimal) : a.Item4[1], a.Item3)).ToArray();
        }
        async public Task<(byte[] member, decimal dist, decimal longitude, decimal latitude, long hash)[]> GeoRadiusBytesAsync(string key, decimal longitude, decimal latitude, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null, bool withCoord = false, bool withDist = false, bool withHash = false)
        {
            var args = new List<object>(new object[] { key, longitude, latitude, radius, unit });
            if (withCoord) args.Add("WITHCOORD");
            if (withDist) args.Add("WITHDIST");
            if (withHash) args.Add("WITHHASH");
            if (count.HasValue) args.Add(count);
            if (sorting.HasValue) args.Add(sorting);

            var cmd = new RedisTuple.Generic<byte[], decimal, long, decimal[]>.Single(
                new RedisBytes(null),
                withDist == false ? null : new RedisFloat(null),
                withHash == false ? null : new RedisInt(null),
                withCoord == false ? null : new RedisArray.Generic<decimal>(new RedisFloat(null)), "GEORADIUS", args.ToArray());
            var ret = await WriteAsync(new RedisArray.Generic<Tuple<byte[], decimal, long, decimal[]>>(cmd));
            return ret.Select(a => (a.Item1, a.Item2, a.Item4 == null ? default(decimal) : a.Item4[0], a.Item4 == null ? default(decimal) : a.Item4[1], a.Item3)).ToArray();
        }
        async public Task<(string member, decimal dist, decimal longitude, decimal latitude, long hash)[]> GeoRadiusByMemberAsync(string key, object member, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null, bool withCoord = false, bool withDist = false, bool withHash = false)
        {
            var args = new List<object>(new object[] { key, member, radius, unit });
            if (withCoord) args.Add("WITHCOORD");
            if (withDist) args.Add("WITHDIST");
            if (withHash) args.Add("WITHHASH");
            if (count.HasValue) args.Add(count);
            if (sorting.HasValue) args.Add(sorting);

            var cmd = new RedisTuple.Generic<string, decimal, long, decimal[]>.Single(
                new RedisString(null),
                withDist == false ? null : new RedisFloat(null),
                withHash == false ? null : new RedisInt(null),
                withCoord == false ? null : new RedisArray.Generic<decimal>(new RedisFloat(null)), "GEORADIUSBYMEMBER", args.ToArray());
            var ret = await WriteAsync(new RedisArray.Generic<Tuple<string, decimal, long, decimal[]>>(cmd));
            return ret.Select(a => (a.Item1, a.Item2, a.Item4 == null ? default(decimal) : a.Item4[0], a.Item4 == null ? default(decimal) : a.Item4[1], a.Item3)).ToArray();
        }
        async public Task<(byte[] member, decimal dist, decimal longitude, decimal latitude, long hash)[]> GeoRadiusBytesByMemberAsync(string key, object member, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null, bool withCoord = false, bool withDist = false, bool withHash = false)
        {
            var args = new List<object>(new object[] { key, member, radius, unit });
            if (withCoord) args.Add("WITHCOORD");
            if (withDist) args.Add("WITHDIST");
            if (withHash) args.Add("WITHHASH");
            if (count.HasValue) args.Add(count);
            if (sorting.HasValue) args.Add(sorting);

            var cmd = new RedisTuple.Generic<byte[], decimal, long, decimal[]>.Single(
                new RedisBytes(null),
                withDist == false ? null : new RedisFloat(null),
                withHash == false ? null : new RedisInt(null),
                withCoord == false ? null : new RedisArray.Generic<decimal>(new RedisFloat(null)), "GEORADIUSBYMEMBER", args.ToArray());
            var ret = await WriteAsync(new RedisArray.Generic<Tuple<byte[], decimal, long, decimal[]>>(cmd));
            return ret.Select(a => (a.Item1, a.Item2, a.Item4 == null ? default(decimal) : a.Item4[0], a.Item4 == null ? default(decimal) : a.Item4[1], a.Item3)).ToArray();
        }
        #endregion
    }
}
#endif