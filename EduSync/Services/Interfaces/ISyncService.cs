using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EduSync.Services.Interfaces
{
    public interface ISyncService
    {
        Task SyncAsync();
    }
}
