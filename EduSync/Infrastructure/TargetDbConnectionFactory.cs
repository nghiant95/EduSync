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
    public class TargetDbConnectionFactory
    {
        private readonly IConfiguration _config;

        public TargetDbConnectionFactory(IConfiguration config)
        {
            _config = config;
        }

        public IDbConnection Create()
        {
            return new SqlConnection(
                _config.GetConnectionString("TargetDb"));
        }
    }
}
