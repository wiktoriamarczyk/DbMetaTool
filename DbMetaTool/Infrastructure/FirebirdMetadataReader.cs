using DbMetaTool.Domain;
using FirebirdSql.Data.FirebirdClient;

namespace DbMetaTool.Infrastructure
{
    public class FirebirdMetadataReader(FbConnection connection)
    {
        readonly FbConnection _connection = connection;

        public List<DomainDefinition> GetDomains()
        {
            // Get only user-defined domains, exclude system fields (RDB$SYSTEM_FLAG = 0) and internal fields (RDB$FIELD_NAME NOT LIKE 'RDB$%')
             const string query = @"
                 SELECT f.RDB$FIELD_NAME, f.RDB$FIELD_TYPE, f.RDB$FIELD_LENGTH, f.RDB$CHARACTER_LENGTH, f.RDB$FIELD_PRECISION, f.RDB$FIELD_SCALE, f.RDB$NULL_FLAG, f.RDB$DEFAULT_SOURCE
                 FROM RDB$FIELDS f
                 WHERE f.RDB$SYSTEM_FLAG = 0 AND f.RDB$FIELD_NAME NOT LIKE 'RDB$%'
                 ORDER BY f.RDB$FIELD_NAME";

            using var cmd = new FbCommand(query, _connection);
            using var reader = cmd.ExecuteReader();
            var domains = new List<DomainDefinition>();

            while (reader.Read())
            {
                var fieldType = reader.GetInt16(1); // RDB$FIELD_TYPE
                var fieldLength = reader.GetInt16(2); // RDB$FIELD_LENGTH
                var characterLength = reader.IsDBNull(3) ? (short?)null : reader.GetInt16(3); // RDB$CHARACTER_LENGTH
                var fieldPrecision = reader.IsDBNull(4) ? (short)0 : reader.GetInt16(4); // RDB$FIELD_PRECISION
                var fieldScale = reader.IsDBNull(5) ? (short)0 : reader.GetInt16(5); // RDB$FIELD_SCALE
                var nullFlag = reader.IsDBNull(6) ? (short)0 : reader.GetInt16(6); // RDB$NULL_FLAG

                var domain = new DomainDefinition
                {
                    Name = reader.GetString(0).Trim(),
                    SqlType = Common.MapFirebirdType(
                        fieldType,
                        fieldLength,
                        characterLength,
                        fieldPrecision,
                        fieldScale),
                    IsNotNull = nullFlag == 1,
                    DefaultSource = reader.IsDBNull(7) ? null : reader.GetString(7).Trim()
                };

                domains.Add(domain);
            }

            return domains;
        }

        public List<TableDefinition> GetTables()
        {
            var tables = new List<TableDefinition>();
            var domainNames = GetUserDomainNames();

            // Get only user tables, exclude views (RDB$VIEW_BLR IS NULL) and system tables (RDB$SYSTEM_FLAG = 0)
            const string tablesQuery = @"
                SELECT r.RDB$RELATION_NAME
                FROM RDB$RELATIONS r
                WHERE r.RDB$SYSTEM_FLAG = 0 AND r.RDB$VIEW_BLR IS NULL
                ORDER BY r.RDB$RELATION_NAME";

            using var tableCmd = new FbCommand(tablesQuery, _connection);
            using var tableReader = tableCmd.ExecuteReader();

            while (tableReader.Read())
            {
                tables.Add(new TableDefinition
                {
                    Name = tableReader.GetString(0).Trim()
                });
            }

            const string columnsQuery = @"
                SELECT
                    rf.RDB$FIELD_NAME,
                    rf.RDB$NULL_FLAG,
                    rf.RDB$DEFAULT_SOURCE,
                    f.RDB$FIELD_TYPE,
                    f.RDB$FIELD_LENGTH,
                    f.RDB$CHARACTER_LENGTH,
                    f.RDB$FIELD_PRECISION,
                    f.RDB$FIELD_SCALE,
                    f.RDB$FIELD_SUB_TYPE,
                    rf.RDB$FIELD_SOURCE
                FROM RDB$RELATION_FIELDS rf
                JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = rf.RDB$FIELD_SOURCE
                WHERE rf.RDB$RELATION_NAME = @tableName
                ORDER BY rf.RDB$FIELD_POSITION";

            foreach (var table in tables)
            {
                using var colCmd = new FbCommand(columnsQuery, _connection);
                colCmd.Parameters.AddWithValue("@tableName", table.Name);

                using var colReader = colCmd.ExecuteReader();
                while (colReader.Read())
                {
                    var columnName = colReader.GetString(0).Trim();
                    var fieldSource = colReader.GetString(9).Trim();
                    var fieldType = colReader.GetInt16(3);
                    var fieldLength = colReader.GetInt16(4);
                    var characterLength = colReader.IsDBNull(5) ? (short?)null : colReader.GetInt16(5);
                    var fieldPrecision = colReader.IsDBNull(6) ? (short)0 : colReader.GetInt16(6);
                    var fieldScale = colReader.IsDBNull(7) ? (short)0 : colReader.GetInt16(7);

                    bool isDomain = domainNames.Contains(fieldSource);

                    string sqlType = isDomain
                        ? fieldSource
                        : Common.MapFirebirdType(
                            fieldType,
                            fieldLength,
                            characterLength,
                            fieldPrecision,
                            fieldScale
                          );

                    table.Columns.Add(new ColumnDefinition
                    {
                        Name = columnName,
                        SqlType = sqlType,
                        IsNotNull = !colReader.IsDBNull(1),
                        DefaultSource = colReader.IsDBNull(2) ? null : colReader.GetString(2).Trim()
                    });
                }
            }

            // Get constraints and map to tables
            var constraintsMap = GetConstraints();
            foreach (var table in tables)
            {
                if (constraintsMap.TryGetValue(table.Name, out var constraints))
                {
                    table.Constraints = constraints;
                }
            }

            return tables;
        }

        public Dictionary<string, List<ConstraintDefinition>> GetConstraints()
        {
             var result = new Dictionary<string, List<ConstraintDefinition>>(StringComparer.OrdinalIgnoreCase);

            // Get PK and UNIQUE constraints
            const string queryConstraints = @"
                 SELECT
                     rc.RDB$RELATION_NAME,
                     rc.RDB$CONSTRAINT_NAME,
                     rc.RDB$CONSTRAINT_TYPE,
                     sg.RDB$FIELD_NAME,
                     sg.RDB$FIELD_POSITION
                 FROM RDB$RELATION_CONSTRAINTS rc
                 JOIN RDB$INDICES i ON i.RDB$INDEX_NAME = rc.RDB$INDEX_NAME
                 JOIN RDB$INDEX_SEGMENTS sg ON sg.RDB$INDEX_NAME = i.RDB$INDEX_NAME
                 WHERE rc.RDB$CONSTRAINT_TYPE IN ('PRIMARY KEY','UNIQUE')
                 ORDER BY rc.RDB$RELATION_NAME, rc.RDB$CONSTRAINT_NAME, sg.RDB$FIELD_POSITION";

            using var cmd = new FbCommand(queryConstraints, connection);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                var tableName = reader.GetString(0).Trim();
                var constraintName = reader.GetString(1).Trim();
                var constraintType = reader.GetString(2).Trim(); // PRIMARY KEY / UNIQUE
                var columnName = reader.GetString(3).Trim();

                if (!result.TryGetValue(tableName, out var list))
                {
                    list = new List<ConstraintDefinition>();
                    result[tableName] = list;
                }

                // Check if constraint already exists
                var constraint = list.FirstOrDefault(c => c.Name == constraintName);
                if (constraint == null)
                {
                    constraint = new ConstraintDefinition
                    {
                        Name = constraintName,
                        Type = constraintType,
                        Columns = new List<string>()
                    };
                    list.Add(constraint);
                }

                constraint.Columns.Add(columnName);
            }

            // Get FK and map to tables
            const string queryFK = @"
                 SELECT
                     rc.RDB$RELATION_NAME,
                     rc.RDB$CONSTRAINT_NAME,
                     sg.RDB$FIELD_NAME,
                     refc.RDB$CONST_NAME_UQ,
                     rc2.RDB$RELATION_NAME AS REFERENCED_TABLE
                 FROM RDB$RELATION_CONSTRAINTS rc
                 JOIN RDB$REF_CONSTRAINTS refc ON refc.RDB$CONSTRAINT_NAME = rc.RDB$CONSTRAINT_NAME
                 JOIN RDB$INDEX_SEGMENTS sg ON sg.RDB$INDEX_NAME = rc.RDB$INDEX_NAME
                 JOIN RDB$RELATION_CONSTRAINTS rc2 ON rc2.RDB$CONSTRAINT_NAME = refc.RDB$CONST_NAME_UQ
                 WHERE rc.RDB$CONSTRAINT_TYPE = 'FOREIGN KEY'
                 ORDER BY rc.RDB$RELATION_NAME, rc.RDB$CONSTRAINT_NAME, sg.RDB$FIELD_POSITION";

            using var fkCmd = new FbCommand(queryFK, connection);
            using var fkReader = fkCmd.ExecuteReader();

            while (fkReader.Read())
            {
                var tableName = fkReader.GetString(0).Trim();
                var constraintName = fkReader.GetString(1).Trim();
                var columnName = fkReader.GetString(2).Trim();
                var referencedConstraintName = fkReader.GetString(3).Trim();
                var referencedTable = fkReader.GetString(4).Trim();

                if (!result.TryGetValue(tableName, out var list))
                {
                    list = new List<ConstraintDefinition>();
                    result[tableName] = list;
                }

                var constraint = list.FirstOrDefault(c => c.Name == constraintName);
                if (constraint == null)
                {
                    constraint = new ConstraintDefinition
                    {
                        Name = constraintName,
                        Type = "FOREIGN KEY",
                        Columns = new List<string>(),
                        ReferencedTable = referencedTable,
                        ReferencedColumns = new List<string>()
                    };
                    list.Add(constraint);
                }

                constraint.Columns.Add(columnName);
                constraint.ReferencedColumns!.Add(columnName); // prosty mapping 1:1
            }

            return result;
        }

        public List<ProcedureDefinition> GetProcedures()
        {
            var procedures = new List<ProcedureDefinition>();
            var domainNames = GetUserDomainNames();

            // Get only user-defined procedures, exclude system procedures (RDB$SYSTEM_FLAG = 0)
            const string procQuery = @"
                SELECT p.RDB$PROCEDURE_NAME, p.RDB$PROCEDURE_SOURCE
                FROM RDB$PROCEDURES p
                WHERE p.RDB$SYSTEM_FLAG = 0
                ORDER BY p.RDB$PROCEDURE_NAME";

            using var procCmd = new FbCommand(procQuery, _connection);
            using var procReader = procCmd.ExecuteReader();

            while (procReader.Read())
            {
                var procName = procReader.GetString(0).Trim();
                var body = procReader.IsDBNull(1) ? string.Empty : procReader.GetString(1).Trim();

                var procedure = new ProcedureDefinition
                {
                    Name = procName,
                    Body = body
                };


                // Get procedure parameters and map to procedures
                const string paramQuery = @"
                SELECT
                    pp.RDB$PARAMETER_NAME,
                    pp.RDB$PROCEDURE_NAME,
                    PP.RDB$PARAMETER_NUMBER,
                    pp.RDB$PARAMETER_TYPE,
                    pp.RDB$FIELD_SOURCE,
                    f.RDB$FIELD_TYPE,
                    f.RDB$FIELD_LENGTH,
                    f.RDB$CHARACTER_LENGTH,
                    f.RDB$FIELD_PRECISION,
                    f.RDB$FIELD_SCALE
                FROM RDB$PROCEDURE_PARAMETERS pp
                JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = pp.RDB$FIELD_SOURCE
                WHERE pp.RDB$PROCEDURE_NAME = @procName
                ORDER BY pp.RDB$PROCEDURE_NAME, pp.RDB$PARAMETER_NUMBER";

                using var paramCmd = new FbCommand(paramQuery, _connection);
                paramCmd.Parameters.AddWithValue("@procName", procName);

                using var paramReader = paramCmd.ExecuteReader();
                while (paramReader.Read())
                {
                    var paramName = paramReader.IsDBNull(0) ? string.Empty : paramReader.GetString(0).Trim();
                    var paramType = paramReader.GetInt16(3); // 0=input, 1=output
                    var fieldSource = paramReader.GetString(4).Trim();
                    var fieldType = paramReader.GetInt16(5);
                    var fieldLength = paramReader.GetInt16(6);
                    var charLength = paramReader.IsDBNull(7) ? (short?)null : paramReader.GetInt16(7);
                    var fieldPrecision = paramReader.IsDBNull(8) ? (short)0 : paramReader.GetInt16(8);
                    var fieldScale = paramReader.IsDBNull(9) ? (short)0 : paramReader.GetInt16(9);

                    string sqlType = domainNames.Contains(fieldSource)
                        ? fieldSource
                        : Common.MapFirebirdType(fieldType, fieldLength, charLength, fieldPrecision, fieldScale);

                    procedure.Parameters.Add(new ProcedureParameter
                    {
                        Name = paramName,
                        SqlType = sqlType,
                        IsOutput = paramType == 1
                    });
                }
                procedures.Add(procedure);
            }

            return procedures;
        }

        HashSet<string> GetUserDomainNames()
        {
            const string query = @"
                SELECT f.RDB$FIELD_NAME
                FROM RDB$FIELDS f
                WHERE f.RDB$SYSTEM_FLAG = 0
                AND f.RDB$FIELD_NAME NOT LIKE 'RDB$%'";

            using var cmd = new FbCommand(query, _connection);
            using var reader = cmd.ExecuteReader();

            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                result.Add(reader.GetString(0).Trim());
            }

            return result;
        }
    }
}
