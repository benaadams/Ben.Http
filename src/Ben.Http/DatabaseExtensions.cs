
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;

namespace Ben.Http
{
    public static class DatabaseExtensions
    {
        public static Task<List<T>> QueryAsync<T>(this IDbConnection conn, string sql)
        {
            throw new NotImplementedException("You need to add the Ben.Http.Generator to use this method.");
        }
    }
}
