using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EduSync.Models
{
    public class SourceKhoa
    {
        public string MaKH { get; set; }

        public string TenKhoa { get; set; }
    }
    public class Department
    {
        public Guid DepartmentID { get; set; }

        public int Order { get; set; }

        public string DepartmentCode { get; set; }

        public string DepartmentName { get; set; }

        public string ShortName { get; set; }

        public string EnglishName { get; set; }

        public string Category { get; set; }

        public string CreatedAt { get; set; }

        public string CreatedBy { get; set; }

        public string UpdatedAt { get; set; }

        public string UpdatedBy { get; set; }

        public bool IsDeleted { get; set; }
    }
}
