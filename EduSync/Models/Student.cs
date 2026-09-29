using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EduSync.Models
{
    public class SourceStudent
    {
        public int Id { get; set; }

        public string StudentCode { get; set; }

        public string FullName { get; set; }

        public string Email { get; set; }
    }
    public class Student
    {
        public Guid Id { get; set; }

        public int SourceId { get; set; }

        public string StudentCode { get; set; }

        public string FullName { get; set; }

        public string Email { get; set; }
    }
}
