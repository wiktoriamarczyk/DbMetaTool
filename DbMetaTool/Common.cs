using DbMetaTool.Domain;
using FirebirdSql.Data.FirebirdClient;
using System.Text;
using System.Text.RegularExpressions;

namespace DbMetaTool
{
    class Common
    {
        // For simplicity, we assume that only domains, tables, and procedures are supported.
        public enum StructureType
        {
            Unknown,
            Domain,
            Table,
            Procedure
        }

        public class ScriptInfo
        {
            public string FileName { get; set; } = string.Empty;
            public string FileContent { get; set; } = string.Empty;
            public StructureType Type { get; set; }
        }

        // Get scripts ordered by type: domains first, then tables, then procedures.
        public static List<ScriptInfo> GetOrderedScripts(string scriptsDirectory)
        {
            var scriptFiles = Directory.GetFiles(scriptsDirectory, "*.sql");

            var scriptsWithType = scriptFiles.Select(f =>
            {
                var content = File.ReadAllText(f).ToUpperInvariant();
                if (content.Contains("CREATE DOMAIN")) return new ScriptInfo { FileName = f, FileContent = content, Type = StructureType.Domain };
                if (content.Contains("CREATE TABLE")) return new ScriptInfo { FileName = f, FileContent = content, Type = StructureType.Table };
                if (content.Contains("CREATE PROCEDURE")) return new ScriptInfo { FileName = f, FileContent = content, Type = StructureType.Procedure };
                return new ScriptInfo { FileName = f, FileContent = f, Type = StructureType.Unknown };
            }).ToList();

            var orderedScripts = scriptsWithType
                .OrderBy(s => s.Type)
                .ToList();

            return orderedScripts;
        }

        // Splits a SQL script into individual statements, taking into account BEGIN...END blocks and ignoring semicolons inside them.
        public static List<string> SplitSqlStatements(string sql)
        {
            sql = RemoveBlockComments(sql);
            sql = RemoveSetSqlDialect(sql);

            var result = new List<string>();
            var sb = new StringBuilder();

            int beginEndLevel = 0;

            var tokens = Regex.Split(sql, @"(\bBEGIN\b|\bEND\b|;)", RegexOptions.IgnoreCase);

            foreach (var rawToken in tokens)
            {
                var token = rawToken;

                if (Regex.IsMatch(token, @"\bBEGIN\b", RegexOptions.IgnoreCase))
                {
                    beginEndLevel++;
                    sb.Append(token);
                    continue;
                }

                if (Regex.IsMatch(token, @"\bEND\b", RegexOptions.IgnoreCase))
                {
                    beginEndLevel = Math.Max(0, beginEndLevel - 1);
                    sb.Append(token);
                    continue;
                }

                if (token == ";")
                {
                    if (beginEndLevel == 0)
                    {
                        // End of statement
                        var statement = sb.ToString().Trim();
                        if (!string.IsNullOrWhiteSpace(statement))
                            result.Add(statement + ";");

                        sb.Clear();
                    }
                    else
                    {
                        // Inside BEGIN...END, treat as normal token
                        sb.Append(token);
                    }

                    continue;
                }

                sb.Append(token);
            }

            // Add any remaining statement
            var tail = sb.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(tail))
                result.Add(tail);

            return result;
        }

        public static string MapFirebirdType(short fieldType, short fieldLength, short? characterLength, short fieldPrecision, short fieldScale)
        {
            return fieldType switch
            {
                7 => "SMALLINT",                  // INTEGER 16-bit
                8 => "INTEGER",                   // INTEGER 32-bit
                10 => "FLOAT",
                12 => "DATE",
                13 => "TIME",
                14 => $"CHAR({characterLength ?? fieldLength})",
                16 => fieldScale == 0 ? "BIGINT" : $"NUMERIC({fieldPrecision},{Math.Abs(fieldScale)})",
                23 => "BOOLEAN",
                24 => "DECFLOAT(16)",
                25 => "DECFLOAT(34)",
                26 => "INT128",
                27 => "DOUBLE PRECISION",
                28 => "TIME WITH TIME ZONE",
                29 => "TIMESTAMP WITH TIME ZONE",
                35 => "TIMESTAMP",
                37 => $"VARCHAR({characterLength ?? fieldLength})",
                261 => "BLOB",
                _ => throw new NotSupportedException($"Unsupported field type: {fieldType}")
            };
        }

        public static bool DomainExists(FbConnection connection, string domainName, FbTransaction? transaction=null)
        {
            const string query = @"SELECT 1 FROM RDB$FIELDS WHERE RDB$FIELD_NAME = @name";
            if (transaction != null)
            {
                using var cmdTrans = new FbCommand(query, connection, transaction);
                cmdTrans.Parameters.AddWithValue("@name", domainName);
                return cmdTrans.ExecuteScalar() != null;
            }
            using var cmd = new FbCommand(query, connection);
            cmd.Parameters.AddWithValue("@name", domainName);
            return cmd.ExecuteScalar() != null;
        }

        public static bool TableExists(FbConnection connection, string tableName, FbTransaction? transaction = null)
        {
            const string query = @"SELECT 1 FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = @name AND RDB$SYSTEM_FLAG = 0";
            if (transaction != null)
            {
                using var cmdTrans = new FbCommand(query, connection, transaction);
                cmdTrans.Parameters.AddWithValue("@name", tableName);
                return cmdTrans.ExecuteScalar() != null;
            }
            using var cmd = new FbCommand(query, connection);
            cmd.Parameters.AddWithValue("@name", tableName);
            return cmd.ExecuteScalar() != null;
        }

        public static bool ProcedureExists(FbConnection connection, string procName, FbTransaction? transaction = null)
        {
            const string query = @"SELECT 1 FROM RDB$PROCEDURES WHERE RDB$PROCEDURE_NAME = @name AND RDB$SYSTEM_FLAG = 0";
            if (transaction != null)
            {
                using var cmdTrans = new FbCommand(query, connection, transaction);
                cmdTrans.Parameters.AddWithValue("@name", procName);
                return cmdTrans.ExecuteScalar() != null;
            }
            using var cmd = new FbCommand(query, connection);
            cmd.Parameters.AddWithValue("@name", procName);
            return cmd.ExecuteScalar() != null;
        }

        public static bool ConstraintExists(FbConnection connection, string tableName, string constraintName, FbTransaction? transaction = null)
        {
            const string query = @"
                SELECT 1
                FROM RDB$RELATION_CONSTRAINTS
                WHERE RDB$RELATION_NAME = @table
                  AND RDB$CONSTRAINT_NAME = @constraint";

            using var cmd = new FbCommand(query, connection, transaction);
            cmd.Parameters.AddWithValue("@table", tableName);
            cmd.Parameters.AddWithValue("@constraint", constraintName);

            return cmd.ExecuteScalar() != null;
        }


        static string RemoveBlockComments(string sql)
        {
            return Regex.Replace(
                sql,
                @"/\*.*?\*/",
                string.Empty,
                RegexOptions.Singleline
            );
        }

        static string RemoveSetSqlDialect(string sql)
        {
            return Regex.Replace(
                sql,
                @"\bSET\s+SQL\s+DIALECT\s+\d+\s*;",
                string.Empty,
                RegexOptions.IgnoreCase
            );
        }
    }
}