
namespace DbMetaTool.Domain
{
    public class DomainDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string SqlType { get; set; } = string.Empty;
        public bool IsNotNull { get; set; }
        public string? DefaultSource { get; set; }
    }

    public class ColumnDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string SqlType { get; set; } = string.Empty;
        public bool IsNotNull { get; set; }
        public string? DefaultSource { get; set; }
    }

    public class ConstraintDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // PRIMARY KEY, UNIQUE, FOREIGN KEY
        public List<string> Columns { get; set; } = new();
        public string? ReferencedTable { get; set; } // dla FK
        public List<string>? ReferencedColumns { get; set; } // dla FK
    }

    public class TableDefinition
    {
        public string Name { get; set; } = string.Empty;
        public List<ColumnDefinition> Columns { get; set; } = new();
        public List<ConstraintDefinition> Constraints { get; set; } = new();
    }

    public class ProcedureDefinition
    {
        public string Name { get; set; } = string.Empty;
        public List<ProcedureParameter> Parameters { get; set; } = new();
        public string Body { get; set; } = string.Empty; // BEGIN ... END
    }

    public class ProcedureParameter
    {
        public string Name { get; set; } = string.Empty;
        public string SqlType { get; set; } = string.Empty;
        public bool IsOutput { get; set; } // false = input, true = output
    }
}
