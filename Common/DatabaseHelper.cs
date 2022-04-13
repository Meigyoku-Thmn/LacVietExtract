using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Common
{
    public static class DatabaseHelper
    {
        public static T Get<T>(this DbDataReader reader, string fieldName)
        {
            var value = reader[fieldName];
            if (value == DBNull.Value)
                return default;
            return (T)value;
        }
    }
}
