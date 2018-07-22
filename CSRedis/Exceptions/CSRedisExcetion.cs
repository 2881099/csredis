using System;
using System.Collections.Generic;
using System.Text;

namespace CSRedisCore.CSRedis.Exceptions
{
    public class CSRedisExcetion:Exception
    {
        public CSRedisExcetion() : base()
        {

        }
        public CSRedisExcetion(string msg):base(msg)
        {

        }
        public CSRedisExcetion(string msg,Exception ex) : base(msg, ex)
        {

        }
    }
}
