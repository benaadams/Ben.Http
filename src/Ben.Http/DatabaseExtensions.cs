
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace Ben.Http
{
    public static class DatabaseExtensions
    {
        public static Task<List<TResult>> QueryAsync<TResult>(this IDbConnection conn, string sql, bool autoClose = true)
        {
            throw new NotImplementedException("You need to add the Ben.Http.Generator to use this method.");
        }

        public static Task<TResult> QueryRowAsync<TResult, TValue>(this IDbConnection conn, string sql, (string name, TValue value) parameter, bool autoClose = true)
        {
            throw new NotImplementedException("You need to add the Ben.Http.Generator to use this method.");
        }
    }
}