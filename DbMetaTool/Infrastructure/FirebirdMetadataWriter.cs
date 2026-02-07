using DbMetaTool.Domain;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Text;

namespace DbMetaTool.Infrastructure
{
    static class FirebirdMetadataWriter
    {
        public static string GenerateDomainsMetadata(IEnumerable<DomainDefinition> domains)
        {
            var sb = new StringBuilder();
            foreach (var domain in domains)
            {
                sb.AppendLine($"CREATE DOMAIN {domain.Name} AS {domain.SqlType}" +
                              (domain.IsNotNull ? " NOT NULL" : "") +
                              (string.IsNullOrEmpty(domain.DefaultSource) ? "" : $" DEFAULT {domain.DefaultSource}") +
                              ";");
            }
            return sb.ToString();
        }

        public static void WriteDomainMetadata(IEnumerable<DomainDefinition> domains, string path)
        {
            File.WriteAllText(path, GenerateDomainsMetadata(domains), Encoding.UTF8);
        }

        public static string GenerateTablesMetadata(IEnumerable<TableDefinition> tables)
        {
            var sb = new StringBuilder();

            foreach (var table in tables)
            {
                sb.AppendLine($"CREATE TABLE {table.Name} (");

                for (int i = 0; i < table.Columns.Count; i++)
                {
                    var c = table.Columns[i];
                    sb.Append($"    {c.Name} {c.SqlType}");

                    if (c.IsNotNull)
                        sb.Append(" NOT NULL");

                    if (!string.IsNullOrEmpty(c.DefaultSource))
                        sb.Append($" DEFAULT {c.DefaultSource}");

                    if (i < table.Columns.Count - 1)
                        sb.Append(",");

                    sb.AppendLine();
                }

                sb.AppendLine(");");
                sb.AppendLine();

                foreach (var constraint in table.Constraints)
                {
                    switch (constraint.Type.ToUpperInvariant())
                    {
                        case "PRIMARY KEY":
                        case "UNIQUE":
                            sb.AppendLine($"ALTER TABLE {table.Name} ADD CONSTRAINT {constraint.Name} {constraint.Type} ({string.Join(", ", constraint.Columns)});");
                            break;

                        case "FOREIGN KEY":
                            if (!string.IsNullOrEmpty(constraint.ReferencedTable) && constraint.ReferencedColumns != null)
                            {
                                sb.AppendLine($"ALTER TABLE {table.Name} ADD CONSTRAINT {constraint.Name} FOREIGN KEY ({string.Join(", ", constraint.Columns)}) REFERENCES {constraint.ReferencedTable} ({string.Join(", ", constraint.ReferencedColumns)});");
                            }
                            break;
                    }
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        public static void WriteTablesMetadata(IEnumerable<TableDefinition> tables, string path)
        {
            string sql = GenerateTablesMetadata(tables);
            File.WriteAllText(path, sql, Encoding.UTF8);
        }

        public static void WriteProceduresMetadata(List<ProcedureDefinition> procedures, string outputPath)
        {
            var sb = new StringBuilder();

            foreach (var proc in procedures)
            {
                sb.AppendLine($"CREATE PROCEDURE {proc.Name}");

                if (proc.Parameters.Any(p => p.IsOutput))
                {
                    sb.AppendLine("RETURNS (");
                    sb.AppendLine(string.Join(",\n", proc.Parameters
                        .Where(p => p.IsOutput)
                        .Select(p => $"    {p.Name} {p.SqlType}")));
                    sb.AppendLine(")");
                }

                sb.AppendLine("AS");
                sb.AppendLine("BEGIN");
                sb.AppendLine("  SUSPEND;");
                sb.AppendLine("END;");
                sb.AppendLine();

                sb.AppendLine($"ALTER PROCEDURE {proc.Name}");
                if (proc.Parameters.Any(p => p.IsOutput))
                {
                    sb.AppendLine("RETURNS (");
                    sb.AppendLine(string.Join(",\n", proc.Parameters
                        .Where(p => p.IsOutput)
                        .Select(p => $"    {p.Name} {p.SqlType}")));
                    sb.AppendLine(")");
                }
                sb.AppendLine("AS");
                sb.AppendLine(proc.Body + ";");
                sb.AppendLine();
            }

            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
        }
    }
}
