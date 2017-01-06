using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CSRedis
{
    /// <summary>
    /// Interface for syncronous RedisClient methods
    /// </summary>
    public interface IRedisClientSync : IRedisClient
    {
        /// <summary>
        /// Connect to the remote host
        /// </summary>
        /// <param name="timeout">Connection timeout in milliseconds</param>
        /// <returns>True if connected</returns>
        bool Connect(int timeout);


        /// <summary>
        /// Call arbitrary Redis command
        /// </summary>
        /// <param name="command">Command name</param>
        /// <param name="args">Command arguments</param>
        /// <returns>Redis object</returns>
        object Call(string command, params string[] args);


        #region Connection
        /// <summary>
        /// Authenticate to the server
        /// </summary>
        /// <param name="password">Redis server password</param>
        /// <returns>Status message</returns>
        string Auth(string password);

        /// <summary>
        /// Echo the given string
        /// </summary>
        /// <param name="message">Message to echo</param>
        /// <returns>Message</returns>
        string Echo(string message);

        /// <summary>
        /// Ping the server
        /// </summary>
        /// <returns>Status message</returns>
        string Ping();

        /// <summary>
        /// Close the connection
        /// </summary>
        /// <returns>Status message</returns>
        string Quit();

        /// <summary>
        /// Change the selected database for the current connection
        /// </summary>
        /// <param name="index">Zero-based database index</param>
        /// <returns>Status message</returns>
        string Select(int index);
        #endregion

        #region Keys
        /// <summary>
        /// Delete a key
        /// </summary>
        /// <param name="keys">Keys to delete</param>
        /// <returns>Number of keys removed</returns>
        long Del(params string[] keys);


        /// <summary>
        /// Return a serialized version of the value stored at the specified key
        /// </summary>
        /// <param name="key">Key to dump</param>
        /// <returns>Serialized value</returns>
        byte[] Dump(string key);


        /// <summary>
        /// Determine if a key exists
        /// </summary>
        /// <param name="key">Key to check</param>
        /// <returns>True if key exists</returns>
        bool Exists(string key);


        /// <summary>
        /// Set a key's time to live in seconds
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="expiration">Expiration (nearest second);</param>
        /// <returns>True if timeout was set; false if key does not exist or timeout could not be set</returns>
        bool Expire(string key, TimeSpan expiration);


        /// <summary>
        /// Set a key's time to live in seconds
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="seconds">Expiration in seconds</param>
        /// <returns>True if timeout was set; false if key does not exist or timeout could not be set</returns>
        bool Expire(string key, int seconds);


        /// <summary>
        /// Set the expiration for a key (nearest second);
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="expirationDate">Date of expiration, to nearest second</param>
        /// <returns>True if timeout was set; false if key does not exist or timeout could not be set</returns>
        bool ExpireAt(string key, DateTime expirationDate);


        /// <summary>
        /// Set the expiration for a key as a UNIX timestamp
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="timestamp">UNIX timestamp</param>
        /// <returns>True if timeout was set; false if key does not exist or timeout could not be set</returns>
        bool ExpireAt(string key, int timestamp);


        /// <summary>
        /// Find all keys matching the given pattern
        /// </summary>
        /// <param name="pattern">Pattern to match</param>
        /// <returns>Array of keys matching pattern</returns>
        string[] Keys(string pattern);


        /// <summary>
        /// Atomically transfer a key from a Redis instance to another one
        /// </summary>
        /// <param name="host">Remote Redis host</param>
        /// <param name="port">Remote Redis port</param>
        /// <param name="key">Key to migrate</param>
        /// <param name="destinationDb">Remote database ID</param>
        /// <param name="timeoutMilliseconds">Timeout in milliseconds</param>
        /// <returns>Status message</returns>
        string Migrate(string host, int port, string key, int destinationDb, int timeoutMilliseconds);


        /// <summary>
        /// Atomically transfer a key from a Redis instance to another one
        /// </summary>
        /// <param name="host">Remote Redis host</param>
        /// <param name="port">Remote Redis port</param>
        /// <param name="key">Key to migrate</param>
        /// <param name="destinationDb">Remote database ID</param>
        /// <param name="timeout">Timeout in milliseconds</param>
        /// <returns>Status message</returns>
        string Migrate(string host, int port, string key, int destinationDb, TimeSpan timeout);


        /// <summary>
        /// Move a key to another database
        /// </summary>
        /// <param name="key">Key to move</param>
        /// <param name="database">Database destination ID</param>
        /// <returns>True if key was moved</returns>
        bool Move(string key, int database);


        /// <summary>
        /// Get the number of references of the value associated with the specified key
        /// </summary>
        /// <param name="arguments">Subcommand arguments</param>
        /// <returns>The type of internal representation used to store the value at the specified key</returns>
        string ObjectEncoding(params string[] arguments);


        /// <summary>
        /// Inspect the internals of Redis objects
        /// </summary>
        /// <param name="subCommand">Type of Object command to send</param>
        /// <param name="arguments">Subcommand arguments</param>
        /// <returns>Varies depending on subCommand</returns>
        long? Object(RedisObjectSubCommand subCommand, params string[] arguments);


        /// <summary>
        /// Remove the expiration from a key
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <returns>True if timeout was removed</returns>
        bool Persist(string key);


        /// <summary>
        /// Set a key's time to live in milliseconds
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="expiration">Expiration (nearest millisecond);</param>
        /// <returns>True if timeout was set</returns>
        bool PExpire(string key, TimeSpan expiration);


        /// <summary>
        /// Set a key's time to live in milliseconds
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="milliseconds">Expiration in milliseconds</param>
        /// <returns>True if timeout was set</returns>
        bool PExpire(string key, long milliseconds);


        /// <summary>
        /// Set the expiration for a key (nearest millisecond);
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="date">Expiration date</param>
        /// <returns>True if timeout was set</returns>
        bool PExpireAt(string key, DateTime date);


        /// <summary>
        /// Set the expiration for a key as a UNIX timestamp specified in milliseconds
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="timestamp">Expiration timestamp (milliseconds);</param>
        /// <returns>True if timeout was set</returns>
        bool PExpireAt(string key, long timestamp);

        /// <summary>
        /// Get the time to live for a key in milliseconds
        /// </summary>
        /// <param name="key">Key to check</param>
        /// <returns>Time-to-live in milliseconds</returns>
        long PTtl(string key);


        /// <summary>
        /// Return a random key from the keyspace
        /// </summary>
        /// <returns>A random key</returns>
        string RandomKey();


        /// <summary>
        /// Rename a key
        /// </summary>
        /// <param name="key">Key to rename</param>
        /// <param name="newKey">New key name</param>
        /// <returns>Status code</returns>
        string Rename(string key, string newKey);


        /// <summary>
        /// Rename a key, only if the new key does not exist
        /// </summary>
        /// <param name="key">Key to rename</param>
        /// <param name="newKey">New key name</param>
        /// <returns>True if key was renamed</returns>
        bool RenameNx(string key, string newKey);


        /// <summary>
        /// Create a key using the provided serialized value, previously obtained using dump
        /// </summary>
        /// <param name="key">Key to restore</param>
        /// <param name="ttl">Time-to-live in milliseconds</param>
        /// <param name="serializedValue">Serialized value from DUMP</param>
        /// <returns>Status code</returns>
        string Restore(string key, long ttl, string serializedValue);

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
        /// <returns>The sorted list</returns>
        string[] Sort(string key, long? offset = null, long? count = null, string by = null, RedisSortDir? dir = null, bool? isAlpha = null, params string[] get);


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
        /// <returns>Number of elements stored</returns>
        long SortAndStore(string key, string destination, long? offset = null, long? count = null, string by = null, RedisSortDir? dir = null, bool? isAlpha = false, params string[] get);


        /// <summary>
        /// Get the time to live for a key
        /// </summary>
        /// <param name="key">Key to check</param>
        /// <returns>Time-to-live in seconds</returns>
        long Ttl(string key);


        /// <summary>
        /// Determine the type stored at key
        /// </summary>
        /// <param name="key">Key to check</param>
        /// <returns>Type of key</returns>
        string Type(string key);


        /// <summary>
        /// Iterate the set of keys in the currently selected Redis database
        /// </summary>
        /// <param name="cursor">The cursor returned by the server in the previous call, or 0 if this is the first call</param>
        /// <param name="pattern">Glob-style pattern to filter returned elements</param>
        /// <param name="count">Set the maximum number of elements to return</param>
        /// <returns>Updated cursor and result set</returns>
        RedisScan<string> Scan(long cursor, string pattern = null, long? count = null);

        #endregion

        #region Hashes
        /// <summary>
        /// Delete one or more hash fields
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <param name="fields">Fields to delete</param>
        /// <returns>Number of fields removed from hash</returns>
        long HDel(string key, params string[] fields);


        /// <summary>
        /// Determine if a hash field exists
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <param name="field">Field to check</param>
        /// <returns>True if hash field exists</returns>
        bool HExists(string key, string field);


        /// <summary>
        /// Get the value of a hash field
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <param name="field">Field to get</param>
        /// <returns>Value of hash field</returns>
        string HGet(string key, string field);


        /// <summary>
        /// Get all the fields and values in a hash
        /// </summary>
        /// <typeparam name="T">Object to map hash</typeparam>
        /// <param name="key">Hash key</param>
        /// <returns>Strongly typed object mapped from hash</returns>
        T HGetAll<T>(string key)
                    where T : class;

        /// <summary>
        /// Get all the fields and values in a hash
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <returns>Dictionary mapped from string</returns>
        Dictionary<string, string> HGetAll(string key);


        /// <summary>
        /// Increment the integer value of a hash field by the given number
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <param name="field">Field to increment</param>
        /// <param name="increment">Increment value</param>
        /// <returns>Value of field after increment</returns>
        long HIncrBy(string key, string field, long increment);


        /// <summary>
        /// Increment the float value of a hash field by the given number
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <param name="field">Field to increment</param>
        /// <param name="increment">Increment value</param>
        /// <returns>Value of field after increment</returns>
        double HIncrByFloat(string key, string field, double increment);


        /// <summary>
        /// Get all the fields in a hash
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <returns>All hash field names</returns>
        string[] HKeys(string key);


        /// <summary>
        /// Get the number of fields in a hash
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <returns>Number of fields in hash</returns>
        long HLen(string key);


        /// <summary>
        /// Get the values of all the given hash fields
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <param name="fields">Fields to return</param>
        /// <returns>Values of given fields</returns>
        string[] HMGet(string key, params string[] fields);


        /// <summary>
        /// Set multiple hash fields to multiple values
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <param name="dict">Dictionary mapping of hash</param>
        /// <returns>Status code</returns>
        string HMSet(string key, Dictionary<string, string> dict);


        /// <summary>
        /// Set multiple hash fields to multiple values
        /// </summary>
        /// <typeparam name="T">Type of object to map hash</typeparam>
        /// <param name="key">Hash key</param>
        /// <param name="obj">Object mapping of hash</param>
        /// <returns>Status code</returns>
        string HMSet<T>(string key, T obj)
                    where T : class;


        /// <summary>
        /// Set multiple hash fields to multiple values
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <param name="keyValues">Array of [key,value,key,value,..]</param>
        /// <returns>Status code</returns>
        string HMSet(string key, params string[] keyValues);


        /// <summary>
        /// Set the value of a hash field
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <param name="field">Hash field to set</param>
        /// <param name="value">Value to set</param>
        /// <returns>True if field is new</returns>
        bool HSet(string key, string field, object value);


        /// <summary>
        /// Set the value of a hash field, only if the field does not exist
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <param name="field">Hash field to set</param>
        /// <param name="value">Value to set</param>
        /// <returns>True if field was set to value</returns>
        bool HSetNx(string key, string field, object value);


        /// <summary>
        /// Get all the values in a hash
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <returns>Array of all values in hash</returns>
        string[] HVals(string key);


        /// <summary>
        /// Iterate the keys and values of a hash field
        /// </summary>
        /// <param name="key">Hash key</param>
        /// <param name="cursor">The cursor returned by the server in the previous call, or 0 if this is the first call</param>
        /// <param name="pattern">Glob-style pattern to filter returned elements</param>
        /// <param name="count">Maximum number of elements to return</param>
        /// <returns>Updated cursor and result set</returns>
        RedisScan<Tuple<string, string>> HScan(string key, long cursor, string pattern = null, long? count = null);

        #endregion

        #region Lists
        /// <summary>
        /// Remove and get the first element and key in a list, or block until one is available
        /// </summary>
        /// <param name="timeout">Timeout in seconds</param>
        /// <param name="keys">List keys</param>
        /// <returns>List key and list value</returns>
        Tuple<string, string> BLPopWithKey(int timeout, params string[] keys);


        /// <summary>
        /// Remove and get the first element and key in a list, or block until one is available
        /// </summary>
        /// <param name="timeout">Timeout in seconds</param>
        /// <param name="keys">List keys</param>
        /// <returns>List key and list value</returns>
        Tuple<string, string> BLPopWithKey(TimeSpan timeout, params string[] keys);


        /// <summary>
        /// Remove and get the first element value in a list, or block until one is available
        /// </summary>
        /// <param name="timeout">Timeout in seconds</param>
        /// <param name="keys">List keys</param>
        /// <returns>List value</returns>
        string BLPop(int timeout, params string[] keys);


        /// <summary>
        /// Remove and get the first element value in a list, or block until one is available
        /// </summary>
        /// <param name="timeout">Timeout in seconds</param>
        /// <param name="keys">List keys</param>
        /// <returns>List value</returns>
        string BLPop(TimeSpan timeout, params string[] keys);


        /// <summary>
        /// Remove and get the last element and key in a list, or block until one is available
        /// </summary>
        /// <param name="timeout">Timeout in seconds</param>
        /// <param name="keys">List keys</param>
        /// <returns>List key and list value</returns>
        Tuple<string, string> BRPopWithKey(int timeout, params string[] keys);


        /// <summary>
        /// Remove and get the last element and key in a list, or block until one is available
        /// </summary>
        /// <param name="timeout">Timeout in seconds</param>
        /// <param name="keys">List keys</param>
        /// <returns>List key and list value</returns>
        Tuple<string, string> BRPopWithKey(TimeSpan timeout, params string[] keys);


        /// <summary>
        /// Remove and get the last element value in a list, or block until one is available
        /// </summary>
        /// <param name="timeout">Timeout in seconds</param>
        /// <param name="keys">List value</param>
        /// <returns></returns>
        string BRPop(int timeout, params string[] keys);


        /// <summary>
        /// Remove and get the last element value in a list, or block until one is available
        /// </summary>
        /// <param name="timeout">Timeout in seconds</param>
        /// <param name="keys">List keys</param>
        /// <returns>List value</returns>
        string BRPop(TimeSpan timeout, params string[] keys);


        /// <summary>
        /// Pop a value from a list, push it to another list and return it; or block until one is available
        /// </summary>
        /// <param name="source">Source list key</param>
        /// <param name="destination">Destination key</param>
        /// <param name="timeout">Timeout in seconds</param>
        /// <returns>Element popped</returns>
        string BRPopLPush(string source, string destination, int timeout);


        /// <summary>
        /// Pop a value from a list, push it to another list and return it; or block until one is available
        /// </summary>
        /// <param name="source">Source list key</param>
        /// <param name="destination">Destination key</param>
        /// <param name="timeout">Timeout in seconds</param>
        /// <returns>Element popped</returns>
        string BRPopLPush(string source, string destination, TimeSpan timeout);


        /// <summary>
        /// Get an element from a list by its index
        /// </summary>
        /// <param name="key">List key</param>
        /// <param name="index">Zero-based index of item to return</param>
        /// <returns>Element at index</returns>
        string LIndex(string key, long index);


        /// <summary>
        /// Insert an element before or after another element in a list
        /// </summary>
        /// <param name="key">List key</param>
        /// <param name="insertType">Relative position</param>
        /// <param name="pivot">Relative element</param>
        /// <param name="value">Element to insert</param>
        /// <returns>Length of list after insert or -1 if pivot not found</returns>
        long LInsert(string key, RedisInsert insertType, string pivot, object value);


        /// <summary>
        /// Get the length of a list
        /// </summary>
        /// <param name="key">List key</param>
        /// <returns>Length of list at key</returns>
        long LLen(string key);


        /// <summary>
        /// Remove and get the first element in a list
        /// </summary>
        /// <param name="key">List key</param>
        /// <returns>First element in list</returns>
        string LPop(string key);


        /// <summary>
        /// Prepend one or multiple values to a list
        /// </summary>
        /// <param name="key">List key</param>
        /// <param name="values">Values to push</param>
        /// <returns>Length of list after push</returns>
        long LPush(string key, params object[] values);

        /// <summary>
        /// Prepend a value to a list, only if the list exists
        /// </summary>
        /// <param name="key">List key</param>
        /// <param name="value">Value to push</param>
        /// <returns>Length of list after push</returns>
        long LPushX(string key, object value);


        /// <summary>
        /// Get a range of elements from a list
        /// </summary>
        /// <param name="key">List key</param>
        /// <param name="start">Start offset</param>
        /// <param name="stop">Stop offset</param>
        /// <returns>List of elements in range</returns>
        string[] LRange(string key, long start, long stop);


        /// <summary>
        /// Remove elements from a list
        /// </summary>
        /// <param name="key">List key</param>
        /// <param name="count">&gt;0: remove N elements from head to tail; &lt;0: remove N elements from tail to head; =0: remove all elements</param>
        /// <param name="value">Remove elements equal to value</param>
        /// <returns>Number of removed elements</returns>
        long LRem(string key, long count, object value);


        /// <summary>
        /// Set the value of an element in a list by its index
        /// </summary>
        /// <param name="key">List key</param>
        /// <param name="index">List index to modify</param>
        /// <param name="value">New element value</param>
        /// <returns>Status code</returns>
        string LSet(string key, long index, object value);

        /// <summary>
        /// Trim a list to the specified range
        /// </summary>
        /// <param name="key">List key</param>
        /// <param name="start">Zero-based start index</param>
        /// <param name="stop">Zero-based stop index</param>
        /// <returns>Status code</returns>
        string LTrim(string key, long start, long stop);

        /// <summary>
        /// Remove and get the last elment in a list
        /// </summary>
        /// <param name="key">List key</param>
        /// <returns>Value of last list element</returns>
        string RPop(string key);


        /// <summary>
        /// Remove the last elment in a list, append it to another list and return it
        /// </summary>
        /// <param name="source">List source key</param>
        /// <param name="destination">Destination key</param>
        /// <returns>Element being popped and pushed</returns>
        string RPopLPush(string source, string destination);

        /// <summary>
        /// Append one or multiple values to a list
        /// </summary>
        /// <param name="key">List key</param>
        /// <param name="values">Values to push</param>
        /// <returns>Length of list after push</returns>
        long RPush(string key, params object[] values);

        /// <summary>
        /// Append a value to a list, only if the list exists
        /// </summary>
        /// <param name="key">List key</param>
        /// <param name="values">Values to push</param>
        /// <returns>Length of list after push</returns>
        long RPushX(string key, params object[] values);
        #endregion

        #region Sets
        /// <summary>
        /// Add one or more members to a set
        /// </summary>
        /// <param name="key">Set key</param>
        /// <param name="members">Members to add to set</param>
        /// <returns>Number of elements added to set</returns>
        long SAdd(string key, params object[] members);

        /// <summary>
        /// Get the number of members in a set
        /// </summary>
        /// <param name="key">Set key</param>
        /// <returns>Number of elements in set</returns>
        long SCard(string key);

        /// <summary>
        /// Subtract multiple sets
        /// </summary>
        /// <param name="keys">Set keys to subtract</param>
        /// <returns>Array of elements in resulting set</returns>
        string[] SDiff(params string[] keys);

        /// <summary>
        /// Subtract multiple sets and store the resulting set in a key
        /// </summary>
        /// <param name="destination">Destination key</param>
        /// <param name="keys">Set keys to subtract</param>
        /// <returns>Number of elements in the resulting set</returns>
        long SDiffStore(string destination, params string[] keys);


        /// <summary>
        /// Intersect multiple sets
        /// </summary>
        /// <param name="keys">Set keys to intersect</param>
        /// <returns>Array of elements in resulting set</returns>
        string[] SInter(params string[] keys);




        /// <summary>
        /// Intersect multiple sets and store the resulting set in a key
        /// </summary>
        /// <param name="destination">Destination key</param>
        /// <param name="keys">Set keys to intersect</param>
        /// <returns>Number of elements in resulting set</returns>
        long SInterStore(string destination, params string[] keys);




        /// <summary>
        /// Determine if a given value is a member of a set
        /// </summary>
        /// <param name="key">Set key</param>
        /// <param name="member">Member to lookup</param>
        /// <returns>True if member exists in set</returns>
        bool SIsMember(string key, object member);




        /// <summary>
        /// Get all the members in a set
        /// </summary>
        /// <param name="key">Set key</param>
        /// <returns>All elements in the set</returns>
        string[] SMembers(string key);




        /// <summary>
        /// Move a member from one set to another
        /// </summary>
        /// <param name="source">Source key</param>
        /// <param name="destination">Destination key</param>
        /// <param name="member">Member to move</param>
        /// <returns>True if element was moved</returns>
        bool SMove(string source, string destination, object member);




        /// <summary>
        /// Remove and
        /// </summary>
        /// <param name="key">Set key</param>
        /// <returns>The removed element</returns>
        string SPop(string key);




        /// <summary>
        /// Get a random member from a set
        /// </summary>
        /// <param name="key">Set key</param>
        /// <returns>One random element from set</returns>
        string SRandMember(string key);




        /// <summary>
        /// Get one or more random members from a set
        /// </summary>
        /// <param name="key">Set key</param>
        /// <param name="count">Number of elements to return</param>
        /// <returns>One or more random elements from set</returns>
        string[] SRandMember(string key, long count);




        /// <summary>
        /// Remove one or more members from a set
        /// </summary>
        /// <param name="key">Set key</param>
        /// <param name="members">Set members to remove</param>
        /// <returns>Number of elements removed from set</returns>
        long SRem(string key, params object[] members);




        /// <summary>
        /// Add multiple sets
        /// </summary>
        /// <param name="keys">Set keys to union</param>
        /// <returns>Array of elements in resulting set</returns>
        string[] SUnion(params string[] keys);




        /// <summary>
        /// Add multiple sets and store the resulting set in a key
        /// </summary>
        /// <param name="destination">Destination key</param>
        /// <param name="keys">Set keys to union</param>
        /// <returns>Number of elements in resulting set</returns>
        long SUnionStore(string destination, params string[] keys);




        /// <summary>
        /// Iterate the elements of a set field
        /// </summary>
        /// <param name="key">Set key</param>
        /// <param name="cursor">The cursor returned by the server in the previous call, or 0 if this is the first call</param>
        /// <param name="pattern">Glob-style pattern to filter returned elements</param>
        /// <param name="count">Maximum number of elements to return</param>
        /// <returns>Updated cursor and result set</returns>
        RedisScan<string> SScan(string key, long cursor, string pattern = null, long? count = null);



        #endregion

        #region Sorted Sets
        /// <summary>
        /// Add one or more members to a sorted set, or update its score if it already exists
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="memberScores">Array of member scores to add to sorted set</param>
        /// <returns>Number of elements added to the sorted set (not including member updates);</returns>
        long ZAdd<TScore, TMember>(string key, params Tuple<TScore, TMember>[] memberScores);




        /// <summary>
        /// Add one or more members to a sorted set, or update its score if it already exists
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="memberScores">Array of member scores [s1, m1, s2, m2, ..]</param>
        /// <returns>Number of elements added to the sorted set (not including member updates);</returns>
        long ZAdd(string key, params string[] memberScores);




        /// <summary>
        /// Get the number of members in a sorted set
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <returns>Number of elements in the sorted set</returns>
        long ZCard(string key);




        /// <summary>
        /// Count the members in a sorted set with scores within the given values
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="min">Minimum score</param>
        /// <param name="max">Maximum score</param>
        /// <param name="exclusiveMin">Minimum score is exclusive</param>
        /// <param name="exclusiveMax">Maximum score is exclusive</param>
        /// <returns>Number of elements in the specified score range</returns>
        long ZCount(string key, double min, double max, bool exclusiveMin = false, bool exclusiveMax = false);




        /// <summary>
        /// Count the members in a sorted set with scores within the given values
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="min">Minimum score</param>
        /// <param name="max">Maximum score</param>
        /// <returns>Number of elements in the specified score range</returns>
        long ZCount(string key, string min, string max);




        /// <summary>
        /// Increment the score of a member in a sorted set
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="increment">Increment by value</param>
        /// <param name="member">Sorted set member to increment</param>
        /// <returns>New score of member</returns>
        double ZIncrBy(string key, double increment, string member);




        /// <summary>
        /// Intersect multiple sorted sets and store the resulting set in a new key
        /// </summary>
        /// <param name="destination">Destination key</param>
        /// <param name="weights">Multiplication factor for each input set</param>
        /// <param name="aggregate">Aggregation function of resulting set</param>
        /// <param name="keys">Sorted set keys to intersect</param>
        /// <returns>Number of elements in the resulting sorted set</returns>
        long ZInterStore(string destination, double[] weights = null, RedisAggregate? aggregate = null, params string[] keys);




        /// <summary>
        /// Intersect multiple sorted sets and store the resulting set in a new key
        /// </summary>
        /// <param name="destination">Destination key</param>
        /// <param name="keys">Sorted set keys to intersect</param>
        /// <returns>Number of elements in the resulting sorted set</returns>
        long ZInterStore(string destination, params string[] keys);




        /// <summary>
        ///
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="start">Start offset</param>
        /// <param name="stop">Stop offset</param>
        /// <param name="withScores">Include scores in result</param>
        /// <returns>Array of elements in the specified range (with optional scores);</returns>
        string[] ZRange(string key, long start, long stop, bool withScores = false);




        /// <summary>
        ///
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="start">Start offset</param>
        /// <param name="stop">Stop offset</param>
        /// <returns>Array of elements in the specified range with scores</returns>
        Tuple<string, double>[] ZRangeWithScores(string key, long start, long stop);




        /// <summary>
        ///
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="min">Minimum score</param>
        /// <param name="max">Maximum score</param>
        /// <param name="withScores">Include scores in result</param>
        /// <param name="exclusiveMin">Minimum score is exclusive</param>
        /// <param name="exclusiveMax">Maximum score is exclusive</param>
        /// <param name="offset">Start offset</param>
        /// <param name="count">Number of elements to return</param>
        /// <returns>List of elements in the specified range (with optional scores);</returns>
        string[] ZRangeByScore(string key, double min, double max, bool withScores = false, bool exclusiveMin = false, bool exclusiveMax = false, long? offset = null, long? count = null);




        /// <summary>
        ///
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="min">Minimum score</param>
        /// <param name="max">Maximum score</param>
        /// <param name="withScores">Include scores in result</param>
        /// <param name="offset">Start offset</param>
        /// <param name="count">Number of elements to return</param>
        /// <returns>List of elements in the specified range (with optional scores);</returns>
        string[] ZRangeByScore(string key, string min, string max, bool withScores = false, long? offset = null, long? count = null);




        /// <summary>
        ///
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="min">Minimum score</param>
        /// <param name="max">Maximum score</param>
        /// <param name="exclusiveMin">Minimum score is exclusive</param>
        /// <param name="exclusiveMax">Maximum score is exclusive</param>
        /// <param name="offset">Start offset</param>
        /// <param name="count">Number of elements to return</param>
        /// <returns>List of elements in the specified range (with optional scores);</returns>
        Tuple<string, double>[] ZRangeByScoreWithScores(string key, double min, double max, bool exclusiveMin = false, bool exclusiveMax = false, long? offset = null, long? count = null);




        /// <summary>
        ///
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="min">Minimum score</param>
        /// <param name="max">Maximum score</param>
        /// <param name="offset">Start offset</param>
        /// <param name="count">Number of elements to return</param>
        /// <returns>List of elements in the specified range (with optional scores);</returns>
        Tuple<string, double>[] ZRangeByScoreWithScores(string key, string min, string max, long? offset = null, long? count = null);




        /// <summary>
        /// Determine the index of a member in a sorted set
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="member">Member to lookup</param>
        /// <returns>Rank of member or null if key does not exist</returns>
        long? ZRank(string key, string member);




        /// <summary>
        /// Remove one or more members from a sorted set
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="members">Members to remove</param>
        /// <returns>Number of elements removed</returns>
        long ZRem(string key, params object[] members);




        /// <summary>
        /// Remove all members in a sorted set within the given indexes
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="start">Start offset</param>
        /// <param name="stop">Stop offset</param>
        /// <returns>Number of elements removed</returns>
        long ZRemRangeByRank(string key, long start, long stop);




        /// <summary>
        /// Remove all members in a sorted set within the given scores
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="min">Minimum score</param>
        /// <param name="max">Maximum score</param>
        /// <param name="exclusiveMin">Minimum score is exclusive</param>
        /// <param name="exclusiveMax">Maximum score is exclusive</param>
        /// <returns>Number of elements removed</returns>
        long ZRemRangeByScore(string key, double min, double max, bool exclusiveMin = false, bool exclusiveMax = false);




        /// <summary>
        ///
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="start">Start offset</param>
        /// <param name="stop">Stop offset</param>
        /// <param name="withScores">Include scores in result</param>
        /// <returns>List of elements in the specified range (with optional scores);</returns>
        string[] ZRevRange(string key, long start, long stop, bool withScores = false);




        /// <summary>
        ///
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="start">Start offset</param>
        /// <param name="stop">Stop offset</param>
        /// <returns>List of elements in the specified range (with optional scores);</returns>
        Tuple<string, double>[] ZRevRangeWithScores(string key, long start, long stop);




        /// <summary>
        ///
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="max">Maximum score</param>
        /// <param name="min">Minimum score</param>
        /// <param name="withScores">Include scores in result</param>
        /// <param name="exclusiveMax">Maximum score is exclusive</param>
        /// <param name="exclusiveMin">Minimum score is exclusive</param>
        /// <param name="offset">Start offset</param>
        /// <param name="count">Number of elements to return</param>
        /// <returns>List of elements in the specified score range (with optional scores);</returns>
        string[] ZRevRangeByScore(string key, double max, double min, bool withScores = false, bool exclusiveMax = false, bool exclusiveMin = false, long? offset = null, long? count = null);




        /// <summary>
        ///
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="max">Maximum score</param>
        /// <param name="min">Minimum score</param>
        /// <param name="withScores">Include scores in result</param>
        /// <param name="offset">Start offset</param>
        /// <param name="count">Number of elements to return</param>
        /// <returns>List of elements in the specified score range (with optional scores);</returns>
        string[] ZRevRangeByScore(string key, string max, string min, bool withScores = false, long? offset = null, long? count = null);




        /// <summary>
        ///
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="max">Maximum score</param>
        /// <param name="min">Minimum score</param>
        /// <param name="exclusiveMax">Maximum score is exclusive</param>
        /// <param name="exclusiveMin">Minimum score is exclusive</param>
        /// <param name="offset">Start offset</param>
        /// <param name="count">Number of elements to return</param>
        /// <returns>List of elements in the specified score range (with optional scores);</returns>
        Tuple<string, double>[] ZRevRangeByScoreWithScores(string key, double max, double min, bool exclusiveMax = false, bool exclusiveMin = false, long? offset = null, long? count = null);




        /// <summary>
        ///
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="max">Maximum score</param>
        /// <param name="min">Minimum score</param>
        /// <param name="offset">Start offset</param>
        /// <param name="count">Number of elements to return</param>
        /// <returns>List of elements in the specified score range (with optional scores);</returns>
        Tuple<string, double>[] ZRevRangeByScoreWithScores(string key, string max, string min, long? offset = null, long? count = null);




        /// <summary>
        /// Determine the index of a member in a sorted set, with scores ordered from high to low
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="member">Member to lookup</param>
        /// <returns>Rank of member, or null if member does not exist</returns>
        long? ZRevRank(string key, string member);




        /// <summary>
        /// Get the score associated with the given member in a sorted set
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="member">Member to lookup</param>
        /// <returns>Score of member, or null if member does not exist</returns>
        double? ZScore(string key, string member);




        /// <summary>
        /// Add multiple sorted sets and store the resulting sorted set in a new key
        /// </summary>
        /// <param name="destination">Destination key</param>
        /// <param name="weights">Multiplication factor for each input set</param>
        /// <param name="aggregate">Aggregation function of resulting set</param>
        /// <param name="keys">Sorted set keys to union</param>
        /// <returns>Number of elements in the resulting sorted set</returns>
        long ZUnionStore(string destination, double[] weights = null, RedisAggregate? aggregate = null, params string[] keys);




        /// <summary>
        /// Add multiple sorted sets and store the resulting sorted set in a new key
        /// </summary>
        /// <param name="destination">Destination key</param>
        /// <param name="keys">Sorted set keys to union</param>
        /// <returns>Number of elements in the resulting sorted set</returns>
        long ZUnionStore(string destination, params string[] keys);




        /// <summary>
        /// Iterate the scores and elements of a sorted set field
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="cursor">The cursor returned by the server in the previous call, or 0 if this is the first call</param>
        /// <param name="pattern">Glob-style pattern to filter returned elements</param>
        /// <param name="count">Maximum number of elements to return</param>
        /// <returns>Updated cursor and result set</returns>
        RedisScan<Tuple<string, double>> ZScan(string key, long cursor, string pattern = null, long? count = null);




        /// <summary>
        /// Retrieve all the elements in a sorted set with a value between min and max
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="min">Lexagraphic start value. Prefix value with '(' to indicate exclusive; '[' to indicate inclusive. Use '-' or '+' to specify infinity.</param>
        /// <param name="max">Lexagraphic stop value. Prefix value with '(' to indicate exclusive; '[' to indicate inclusive. Use '-' or '+' to specify infinity.</param>
        /// <param name="offset">Limit result set by offset</param>
        /// <param name="count">Limimt result set by size</param>
        /// <returns>List of elements in the specified range</returns>
        string[] ZRangeByLex(string key, string min, string max, long? offset = null, long? count = null);




        /// <summary>
        /// Remove all elements in the sorted set with a value between min and max
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="min">Lexagraphic start value. Prefix value with '(' to indicate exclusive; '[' to indicate inclusive. Use '-' or '+' to specify infinity.</param>
        /// <param name="max">Lexagraphic stop value. Prefix value with '(' to indicate exclusive; '[' to indicate inclusive. Use '-' or '+' to specify infinity.</param>
        /// <returns>Number of elements removed</returns>
        long ZRemRangeByLex(string key, string min, string max);




        /// <summary>
        /// Returns the number of elements in the sorted set with a value between min and max.
        /// </summary>
        /// <param name="key">Sorted set key</param>
        /// <param name="min">Lexagraphic start value. Prefix value with '(' to indicate exclusive; '[' to indicate inclusive. Use '-' or '+' to specify infinity.</param>
        /// <param name="max">Lexagraphic stop value. Prefix value with '(' to indicate exclusive; '[' to indicate inclusive. Use '-' or '+' to specify infinity.</param>
        /// <returns>Number of elements in the specified score range</returns>
        long ZLexCount(string key, string min, string max);



        #endregion

        #region Pub/Sub
        /// <summary>
        /// Listen for messages published to channels matching the given patterns
        /// </summary>
        /// <param name="channelPatterns">Patterns to subscribe</param>
        void PSubscribe(params string[] channelPatterns);



        /// <summary>
        /// Post a message to a channel
        /// </summary>
        /// <param name="channel">Channel to post message</param>
        /// <param name="message">Message to send</param>
        /// <returns>Number of clients that received the message</returns>
        long Publish(string channel, string message);




        /// <summary>
        /// List the currently active channels
        /// </summary>
        /// <param name="pattern">Return only channels matching this pattern</param>
        /// <returns>Array of channel names</returns>
        string[] PubSubChannels(string pattern = null);




        /// <summary>
        ///
        /// </summary>
        /// <param name="channels">Channel names</param>
        /// <returns>Array of channel/count tuples</returns>
        Tuple<string, long>[] PubSubNumSub(params string[] channels);




        /// <summary>
        ///
        /// </summary>
        /// <returns>Number of patterns all clients are subscribed to</returns>
        long PubSubNumPat();




        /// <summary>
        /// Stop listening for messages posted to channels matching the given patterns
        /// </summary>
        /// <param name="channelPatterns">Patterns to unsubscribe</param>
        void PUnsubscribe(params string[] channelPatterns);



        /// <summary>
        /// Listen for messages published to the given channels
        /// </summary>
        /// <param name="channels">Channels to subscribe</param>
        void Subscribe(params string[] channels);



        /// <summary>
        /// Stop listening for messages posted to the given channels
        /// </summary>
        /// <param name="channels">Channels to unsubscribe</param>
        void Unsubscribe(params string[] channels);



        #endregion

        #region Scripting
        /// <summary>
        /// Execute a Lua script server side
        /// </summary>
        /// <param name="script">Script to run on server</param>
        /// <param name="keys">Keys used by script</param>
        /// <param name="arguments">Arguments to pass to script</param>
        /// <returns>Redis object</returns>
        object Eval(string script, string[] keys, params string[] arguments);




        /// <summary>
        /// Execute a Lua script server side, sending only the script's cached SHA hash
        /// </summary>
        /// <param name="sha1">SHA1 hash of script</param>
        /// <param name="keys">Keys used by script</param>
        /// <param name="arguments">Arguments to pass to script</param>
        /// <returns>Redis object</returns>
        object EvalSHA(string sha1, string[] keys, params string[] arguments);




        /// <summary>
        /// Check existence of script SHA hashes in the script cache
        /// </summary>
        /// <param name="scripts">SHA1 script hashes</param>
        /// <returns>Array of boolean values indicating script existence on server</returns>
        bool[] ScriptExists(params string[] scripts);




        /// <summary>
        /// Remove all scripts from the script cache
        /// </summary>
        /// <returns>Status code</returns>
        string ScriptFlush();




        /// <summary>
        /// Kill the script currently in execution
        /// </summary>
        /// <returns>Status code</returns>
        string ScriptKill();




        /// <summary>
        /// Load the specified Lua script into the script cache
        /// </summary>
        /// <param name="script">Lua script to load</param>
        /// <returns>SHA1 hash of script</returns>
        string ScriptLoad(string script);



        #endregion

        #region Strings
        /// <summary>
        /// Append a value to a key
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="value">Value to append to key</param>
        /// <returns>Length of string after append</returns>
        long Append(string key, object value);




        /// <summary>
        /// Count set bits in a string
        /// </summary>
        /// <param name="key">Key to check</param>
        /// <param name="start">Start offset</param>
        /// <param name="end">Stop offset</param>
        /// <returns>Number of bits set to 1</returns>
        long BitCount(string key, long? start = null, long? end = null);




        /// <summary>
        /// Perform bitwise operations between strings
        /// </summary>
        /// <param name="operation">Bit command to execute</param>
        /// <param name="destKey">Store result in destination key</param>
        /// <param name="keys">Keys to operate</param>
        /// <returns>Size of string stored in the destination key</returns>
        long BitOp(RedisBitOp operation, string destKey, params string[] keys);




        /// <summary>
        /// Find first bit set or clear in a string
        /// </summary>
        /// <param name="key">Key to examine</param>
        /// <param name="bit">Bit value (1 or 0);</param>
        /// <param name="start">Examine string at specified byte offset</param>
        /// <param name="end">Examine string to specified byte offset</param>
        /// <returns>Position of the first bit set to the specified value</returns>
        long BitPos(string key, bool bit, long? start = null, long? end = null);




        /// <summary>
        /// Decrement the integer value of a key by one
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <returns>Value of key after decrement</returns>
        long Decr(string key);




        /// <summary>
        /// Decrement the integer value of a key by the given number
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="decrement">Decrement value</param>
        /// <returns>Value of key after decrement</returns>
        long DecrBy(string key, long decrement);




        /// <summary>
        /// Get the value of a key
        /// </summary>
        /// <param name="key">Key to lookup</param>
        /// <returns>Value of key</returns>
        string Get(string key);




        /// <summary>
        /// Returns the bit value at offset in the string value stored at key
        /// </summary>
        /// <param name="key">Key to lookup</param>
        /// <param name="offset">Offset of key to check</param>
        /// <returns>Bit value stored at offset</returns>
        bool GetBit(string key, uint offset);




        /// <summary>
        /// Get a substring of the string stored at a key
        /// </summary>
        /// <param name="key">Key to lookup</param>
        /// <param name="start">Start offset</param>
        /// <param name="end">End offset</param>
        /// <returns>Substring in the specified range</returns>
        string GetRange(string key, long start, long end);




        /// <summary>
        /// Set the string value of a key and
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="value">Value to set</param>
        /// <returns>Old value stored at key, or null if key did not exist</returns>
        string GetSet(string key, object value);




        /// <summary>
        /// Increment the integer value of a key by one
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <returns>Value of key after increment</returns>
        long Incr(string key);




        /// <summary>
        /// Increment the integer value of a key by the given amount
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="increment">Increment amount</param>
        /// <returns>Value of key after increment</returns>
        long IncrBy(string key, long increment);




        /// <summary>
        /// Increment the float value of a key by the given amount
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="increment">Increment amount</param>
        /// <returns>Value of key after increment</returns>
        double IncrByFloat(string key, double increment);




        /// <summary>
        /// Get the values of all the given keys
        /// </summary>
        /// <param name="keys">Keys to lookup</param>
        /// <returns>Array of values at the specified keys</returns>
        string[] MGet(params string[] keys);




        /// <summary>
        /// Set multiple keys to multiple values
        /// </summary>
        /// <param name="keyValues">Key values to set</param>
        /// <returns>Status code</returns>
        string MSet(params Tuple<string, string>[] keyValues);




        /// <summary>
        /// Set multiple keys to multiple values
        /// </summary>
        /// <param name="keyValues">Key values to set [k1, v1, k2, v2, ..]</param>
        /// <returns>Status code</returns>
        string MSet(params string[] keyValues);




        /// <summary>
        /// Set multiple keys to multiple values, only if none of the keys exist
        /// </summary>
        /// <param name="keyValues">Key values to set</param>
        /// <returns>True if all keys were set</returns>
        bool MSetNx(params Tuple<string, string>[] keyValues);




        /// <summary>
        /// Set multiple keys to multiple values, only if none of the keys exist
        /// </summary>
        /// <param name="keyValues">Key values to set [k1, v1, k2, v2, ..]</param>
        /// <returns>True if all keys were set</returns>
        bool MSetNx(params string[] keyValues);




        /// <summary>
        /// Set the value and expiration in milliseconds of a key
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="milliseconds">Expiration in milliseconds</param>
        /// <param name="value">Value to set</param>
        /// <returns>Status code</returns>
        string PSetEx(string key, long milliseconds, object value);




        /// <summary>
        /// Set the string value of a key
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="value">Value to set</param>
        /// <returns>Status code</returns>
        string Set(string key, object value);




        /// <summary>
        /// Set the string value of a key with atomic expiration and existence condition
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="value">Value to set</param>
        /// <param name="expiration">Set expiration to nearest millisecond</param>
        /// <param name="condition">Set key if existence condition</param>
        /// <returns>Status code, or null if condition not met</returns>
        string Set(string key, object value, TimeSpan expiration, RedisExistence? condition = null);




        /// <summary>
        /// Set the string value of a key with atomic expiration and existence condition
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="value">Value to set</param>
        /// <param name="expirationSeconds">Set expiration to nearest second</param>
        /// <param name="condition">Set key if existence condition</param>
        /// <returns>Status code, or null if condition not met</returns>
        string Set(string key, object value, int? expirationSeconds = null, RedisExistence? condition = null);




        /// <summary>
        /// Set the string value of a key with atomic expiration and existence condition
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="value">Value to set</param>
        /// <param name="expirationMilliseconds">Set expiration to nearest millisecond</param>
        /// <param name="condition">Set key if existence condition</param>
        /// <returns>Status code, or null if condition not met</returns>
        string Set(string key, object value, long? expirationMilliseconds = null, RedisExistence? condition = null);




        /// <summary>
        /// Sets or clears the bit at offset in the string value stored at key
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="offset">Modify key at offset</param>
        /// <param name="value">Value to set (on or off);</param>
        /// <returns>Original bit stored at offset</returns>
        bool SetBit(string key, uint offset, bool value);




        /// <summary>
        /// Set the value and expiration of a key
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="seconds">Expiration in seconds</param>
        /// <param name="value">Value to set</param>
        /// <returns>Status code</returns>
        string SetEx(string key, long seconds, object value);




        /// <summary>
        /// Set the value of a key, only if the key does not exist
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="value">Value to set</param>
        /// <returns>True if key was set</returns>
        bool SetNx(string key, object value);




        /// <summary>
        /// Overwrite part of a string at key starting at the specified offset
        /// </summary>
        /// <param name="key">Key to modify</param>
        /// <param name="offset">Start offset</param>
        /// <param name="value">Value to write at offset</param>
        /// <returns>Length of string after operation</returns>
        long SetRange(string key, uint offset, object value);




        /// <summary>
        /// Get the length of the value stored in a key
        /// </summary>
        /// <param name="key">Key to lookup</param>
        /// <returns>Length of string at key</returns>
        long StrLen(string key);



        #endregion

        #region Server
        /// <summary>
        /// Asyncronously rewrite the append-only file
        /// </summary>
        /// <returns>Status code</returns>
        string BgRewriteAof();




        /// <summary>
        /// Asynchronously save the dataset to disk
        /// </summary>
        /// <returns>Status code</returns>
        string BgSave();




        /// <summary>
        /// Kill the connection of a client
        /// </summary>
        /// <param name="ip">Client IP returned from CLIENT LIST</param>
        /// <param name="port">Client port returned from CLIENT LIST</param>
        /// <returns>Status code</returns>
        string ClientKill(string ip, int port);




        /// <summary>
        /// Kill the connection of a client
        /// </summary>
        /// <param name="addr">client's ip:port</param>
        /// <param name="id">client's unique ID</param>
        /// <param name="type">client type (normal|slave|pubsub);</param>
        /// <param name="skipMe">do not kill the calling client</param>
        /// <returns>Nummber of clients killed</returns>
        long ClientKill(string addr = null, string id = null, string type = null, bool? skipMe = null);




        /// <summary>
        /// Get the list of client connections
        /// </summary>
        /// <returns>Formatted string of clients</returns>
        string ClientList();




        /// <summary>
        /// Suspend all Redis clients for the specified amount of time
        /// </summary>
        /// <param name="milliseconds">Time to pause in milliseconds</param>
        /// <returns>Status code</returns>
        string ClientPause(int milliseconds);




        /// <summary>
        /// Suspend all Redis clients for the specified amount of time
        /// </summary>
        /// <param name="timeout">Time to pause</param>
        /// <returns>Status code</returns>
        string ClientPause(TimeSpan timeout);




        /// <summary>
        /// Get the current connection name
        /// </summary>
        /// <returns>Connection name</returns>
        string ClientGetName();




        /// <summary>
        /// Set the current connection name
        /// </summary>
        /// <param name="connectionName">Name of connection (no spaces);</param>
        /// <returns>Status code</returns>
        string ClientSetName(string connectionName);




        /// <summary>
        /// Get the value of a configuration paramter
        /// </summary>
        /// <param name="parameter">Configuration parameter to lookup</param>
        /// <returns>Configuration value</returns>
        Tuple<string, string>[] ConfigGet(string parameter);




        /// <summary>
        /// Reset the stats returned by INFO
        /// </summary>
        /// <returns>Status code</returns>
        string ConfigResetStat();




        /// <summary>
        /// Rewrite the redis.conf file the server was started with, applying the minimal changes needed to make it reflect current configuration
        /// </summary>
        /// <returns>Status code</returns>
        string ConfigRewrite();




        /// <summary>
        /// Set a configuration parameter to the given value
        /// </summary>
        /// <param name="parameter">Parameter to set</param>
        /// <param name="value">Value to set</param>
        /// <returns>Status code</returns>
        string ConfigSet(string parameter, string value);




        /// <summary>
        ///
        /// </summary>
        /// <returns>Number of keys</returns>
        long DbSize();




        /// <summary>
        /// Make the server crash :(
        /// </summary>
        /// <returns>Status code</returns>
        string DebugSegFault();




        /// <summary>
        /// Remove all keys from all databases
        /// </summary>
        /// <returns>Status code</returns>
        string FlushAll();




        /// <summary>
        /// Remove all keys from the current database
        /// </summary>
        /// <returns>Status code</returns>
        string FlushDb();




        /// <summary>
        /// Get information and statistics about the server
        /// </summary>
        /// <param name="section">all|default|server|clients|memory|persistence|stats|replication|cpu|commandstats|cluster|keyspace</param>
        /// <returns>Formatted string</returns>
        string Info(string section = null);




        /// <summary>
        /// Get the timestamp of the last successful save to disk
        /// </summary>
        /// <returns>Date of last save</returns>
        DateTime LastSave();




        /// <summary>
        /// Listen for all requests received by the server in real time
        /// </summary>
        /// <returns>Status code</returns>
        string Monitor();




        /// <summary>
        /// Get role information for the current Redis instance
        /// </summary>
        /// <returns>RedisMasterRole|RedisSlaveRole|RedisSentinelRole</returns>
        RedisRole Role();




        /// <summary>
        /// Syncronously save the dataset to disk
        /// </summary>
        /// <returns>Status code</returns>
        string Save();




        /// <summary>
        /// Syncronously save the dataset to disk an then shut down the server
        /// </summary>
        /// <param name="save">Force a DB saving operation even if no save points are configured</param>
        /// <returns>Status code</returns>
        string Shutdown(bool? save = null);




        /// <summary>
        /// Make the server a slave of another instance or promote it as master
        /// </summary>
        /// <param name="host">Master host</param>
        /// <param name="port">master port</param>
        /// <returns>Status code</returns>
        string SlaveOf(string host, int port);




        /// <summary>
        /// Turn off replication, turning the Redis server into a master
        /// </summary>
        /// <returns>Status code</returns>
        string SlaveOfNoOne();




        /// <summary>
        /// Get latest entries from the slow log
        /// </summary>
        /// <param name="count">Limit entries returned</param>
        /// <returns>Slow log entries</returns>
        RedisSlowLogEntry[] SlowLogGet(long? count = null);




        /// <summary>
        /// Get the length of the slow log
        /// </summary>
        /// <returns>Slow log length</returns>
        long SlowLogLen();




        /// <summary>
        /// Reset the slow log
        /// </summary>
        /// <returns>Status code</returns>
        string SlowLogReset();




        /// <summary>
        /// Internal command used for replication
        /// </summary>
        /// <returns>Byte array of Redis sync data</returns>
        byte[] Sync();




        /// <summary>
        ///
        /// </summary>
        /// <returns>Server time</returns>
        DateTime Time();



        #endregion

        #region Transactions
        /// <summary>
        /// Discard all commands issued after MULTI
        /// </summary>
        /// <returns>Status code</returns>
        string Discard();




        /// <summary>
        /// Execute all commands issued after MULTI
        /// </summary>
        /// <returns>Array of output from all transaction commands</returns>
        object[] Exec();




        /// <summary>
        /// Mark the start of a transaction block
        /// </summary>
        /// <returns>Status code</returns>
        string Multi();




        /// <summary>
        /// Forget about all watched keys
        /// </summary>
        /// <returns>Status code</returns>
        string Unwatch();




        /// <summary>
        /// Watch the given keys to determine execution of the MULTI/EXEC block
        /// </summary>
        /// <param name="keys">Keys to watch</param>
        /// <returns>Status code</returns>
        string Watch(params string[] keys);



        #endregion

        #region HyperLogLog
        /// <summary>
        /// Adds the specified elements to the specified HyperLogLog.
        /// </summary>
        /// <param name="key">Key to update</param>
        /// <param name="elements">Elements to add</param>
        /// <returns>1 if at least 1 HyperLogLog internal register was altered. 0 otherwise.</returns>
        bool PfAdd(string key, params object[] elements);




        /// <summary>
        ///
        /// </summary>
        /// <param name="keys">One or more HyperLogLog keys to examine</param>
        /// <returns>Approximated number of unique elements observed via PFADD</returns>
        long PfCount(params string[] keys);




        /// <summary>
        /// Merge N different HyperLogLogs into a single key.
        /// </summary>
        /// <param name="destKey">Where to store the merged HyperLogLogs</param>
        /// <param name="sourceKeys">The HyperLogLogs keys that will be combined</param>
        /// <returns>Status code</returns>
        string PfMerge(string destKey, params string[] sourceKeys);



        #endregion
    }
}
