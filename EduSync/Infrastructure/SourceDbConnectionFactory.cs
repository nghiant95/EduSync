using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EduSync.Infrastructure
{
    public class SourceDbConnectionFactory
    {
        private readonly IConfiguration _config;

        public SourceDbConnectionFactory(IConfiguration config)
        {
            _config = config;
        }

        public IDbConnection Create()
        {
            var connStr = _config.GetConnectionString("SourceDb");

            //Console.WriteLine(
            //    $"SourceDb = {connStr}");

            return new SqlConnection(connStr);
        }
    }
}
