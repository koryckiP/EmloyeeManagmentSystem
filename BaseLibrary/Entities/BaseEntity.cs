using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BaseLibrary.Entities
{
    public class BaseEntity
    {
        public int Id { get; set; }
        public string? Name { get; set; }

        //Relation: 1 - *
        [JsonIgnore]
        public List<Employee>? Employees { get; set; }
    }
}
