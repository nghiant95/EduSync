using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EduSync.Models
{
    public class SourceCanBo
    {
        public string MaCB { get; set; }
        public string TenCB { get; set; }
        public string MaBM { get; set; } // = SubDepartmentCode
        public string CanBoTG { get; set; } //F = false => cơ hữu, T = true => thỉnh giảng
        public string EmailCB1 { get; set; }
        public string TelCB1 { get; set; }
        public string ngaysinh { get; set; } // = DOB
        public int PhaiNV { get; set; } // = 1 = nữ, 0= nam
        public string HocHam { get; set; } // = Title
        public string HocVi { get; set; } // = Degree

    }
    public class Instructor
    {
        public string Code { get; set; }
        public string Fullname { get; set; }
        public string Gender { get; set; }
        public string Type { get; set; }
        public string DOB { get; set; }
        public string Title { get; set; }
        public string Degree { get; set; }
        public string Nationality { get; set; }
        public string Email { get; set; }
        public string SubDepartmentCode { get; set; } // bộ môn

        public Guid DepartmentID { get; set; }
    }
}
