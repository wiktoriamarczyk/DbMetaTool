using DbMetaTool;
using DbMetaTool.Infrastructure;
using FirebirdSql.Data.FirebirdClient;
using FirebirdSql.Data.Isql;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Text;
using System.Text.RegularExpressions;

namespace DbMetaTool
{
    public static class Program
    {
        // Przykładowe wywołania:
        // DbMetaTool build-db --db-dir "C:\db\fb5" --scripts-dir "C:\scripts"
        // DbMetaTool export-scripts --connection-string "..." --output-dir "C:\out"
        // DbMetaTool update-db --connection-string "..." --scripts-dir "C:\scripts"
        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Użycie:");
                Console.WriteLine("  build-db --db-dir <ścieżka> --scripts-dir <ścieżka>");
                Console.WriteLine("  export-scripts --connection-string <connStr> --output-dir <ścieżka>");
                Console.WriteLine("  update-db --connection-string <connStr> --scripts-dir <ścieżka>");
                return 1;
            }

            try
            {
                var command = args[0].ToLowerInvariant();

                switch (command)
                {
                    case "build-db":
                    {
                        string dbDir = GetArgValue(args, "--db-dir");
                        string scriptsDir = GetArgValue(args, "--scripts-dir");

                        BuildDatabase(dbDir, scriptsDir);
                        Console.WriteLine("Baza danych została zbudowana pomyślnie.");
                        return 0;
                    }

                    case "export-scripts":
                    {
                        string connStr = GetArgValue(args, "--connection-string");
                        string outputDir = GetArgValue(args, "--output-dir");

                        ExportScripts(connStr, outputDir);
                        Console.WriteLine("Skrypty zostały wyeksportowane pomyślnie.");
                        return 0;
                    }

                    case "update-db":
                    {
                        string connStr = GetArgValue(args, "--connection-string");
                        string scriptsDir = GetArgValue(args, "--scripts-dir");

                        UpdateDatabase(connStr, scriptsDir);
                        Console.WriteLine("Baza danych została zaktualizowana pomyślnie.");
                        return 0;
                    }

                    default:
                        Console.WriteLine($"Nieznane polecenie: {command}");
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Błąd: " + ex.Message);
                return -1;
            }
        }

        private static string GetArgValue(string[] args, string name)
        {
            int idx = Array.IndexOf(args, name);
            if (idx == -1 || idx + 1 >= args.Length)
                throw new ArgumentException($"Brak wymaganego parametru {name}");
            return args[idx + 1];
        }

        /// <summary>
        /// Buduje nową bazę danych Firebird 5.0 na podstawie skryptów.
        /// </summary>
        public static void BuildDatabase(string databaseDirectory, string scriptsDirectory)
        {
            // TODO:
            // 1) Utwórz pustą bazę danych FB 5.0 w katalogu databaseDirectory.
            // 2) Wczytaj i wykonaj kolejno skrypty z katalogu scriptsDirectory
            //    (tylko domeny, tabele, procedury).
            // 3) Obsłuż błędy i wyświetl raport.

            // Validate data
            if (!Directory.Exists(scriptsDirectory))
                throw new DirectoryNotFoundException($"Katalog ze skryptami nie istnieje: {scriptsDirectory}");

            if (!Directory.Exists(databaseDirectory))
            {
                Console.WriteLine($"Katalog, w którym ma zostać wygenerowana baza danych, nie istnieje - tworzenie");
                Directory.CreateDirectory(databaseDirectory);
            }

            var orderedScripts = Common.GetOrderedScripts(scriptsDirectory);
            if (orderedScripts.Count == 0)
                throw new FileNotFoundException($"Brak skryptów .sql w katalogu: {scriptsDirectory}");

            Console.WriteLine($"Skrypty do przetworzenia: {orderedScripts.Count}...");


            // Create and connect to the database
            var databasePath = Path.Combine(databaseDirectory, "new_database.fdb");
            DatabaseConfig config = new DatabaseConfig();
            string connectionString = config.GetFbConnectionString(databasePath);

            FbConnection.CreateDatabase(connectionString, config.PageSize, true, true);
            Console.WriteLine("Utworzono bazę danych");

            using var connection = new FbConnection(connectionString);
            connection.Open();


            // Execute scripts
            foreach (var script in orderedScripts)
            {
                // If multiple statements are present, split them and execute separately
                foreach (var statement in Common.SplitSqlStatements(script.FileContent))
                {
                    using var command = new FbCommand(statement, connection);
                    command.ExecuteNonQuery();
                }
            }

        }

        /// <summary>
        /// Generuje skrypty metadanych z istniejącej bazy danych Firebird 5.0.
        /// </summary>
        public static void ExportScripts(string connectionString, string outputDirectory)
        {
            // TODO:
            // 1) Połącz się z bazą danych przy użyciu connectionString.
            // 2) Pobierz metadane domen, tabel (z kolumnami) i procedur.
            // 3) Wygeneruj pliki .sql / .json / .txt w outputDirectory.

            // Validate data
            if (!Directory.Exists(outputDirectory))
            {
                Console.WriteLine(
                    $"Katalog, w którym mają zostać zapisane wygenerowane pliki, nie istnieje  - tworzenie");
                Directory.CreateDirectory(outputDirectory);
            }

            // Connect to the database
            using var connection = new FbConnection(connectionString);
            connection.Open();

            FirebirdMetadataReader metadataReader = new FirebirdMetadataReader(connection);

            // Export domains
            var domains = metadataReader.GetDomains();
            FirebirdMetadataWriter.WriteDomainMetadata(domains, Path.Combine(outputDirectory, "domains.sql"));
            Console.WriteLine($"Wyeksportowano domeny: {domains.Count()}");

            // Export tables and columns
            var tables = metadataReader.GetTables();
            FirebirdMetadataWriter.WriteTablesMetadata(tables, Path.Combine(outputDirectory, "tables.sql"));
            Console.WriteLine($"Wyeksportowano tabele: {tables.Count()}");

            // Export procedures
            var procedures = metadataReader.GetProcedures();
            FirebirdMetadataWriter.WriteProceduresMetadata(procedures, Path.Combine(outputDirectory, "procedures.sql"));
            Console.WriteLine($"Wyeksportowano procedury: {procedures.Count()}");
        }

        /// <summary>
        /// Aktualizuje istniejącą bazę danych Firebird 5.0 na podstawie skryptów.
        /// </summary>
        public static void UpdateDatabase(string connectionString, string scriptsDirectory)
        {
            // TODO:
            // 1) Połącz się z bazą danych przy użyciu connectionString.
            // 2) Wykonaj skrypty z katalogu scriptsDirectory (tylko obsługiwane elementy).
            // 3) Zadbaj o poprawną kolejność i bezpieczeństwo zmian.

            if (!Directory.Exists(scriptsDirectory))
            {
                throw new DirectoryNotFoundException($"Katalog ze skryptami nie istnieje: {scriptsDirectory}");
            }

            var orderedScripts = Common.GetOrderedScripts(scriptsDirectory);
            if (orderedScripts.Count == 0)
                throw new FileNotFoundException($"Brak skryptów .sql w katalogu: {scriptsDirectory}");

            Console.WriteLine($"Skrypty do przetworzenia: {orderedScripts.Count}...");

            // Connect to the database
            using var connection = new FbConnection(connectionString);
            connection.Open();

            // Execute scripts
            foreach (var script in orderedScripts)
            {
                foreach (var sql in Common.SplitSqlStatements(script.FileContent))
                {
                    using var transaction = connection.BeginTransaction();
                    using var cmd = new FbCommand(sql, connection, transaction);

                    try
                    {
                        cmd.ExecuteNonQuery();
                        transaction.Commit();
                        Console.WriteLine($"===Wykonano {sql.Split('\n')[0]}\n");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        Console.WriteLine($"Polecenie nie wykonane\nPowód: {ex.Message}\n");
                    }

                }
            }
        }
    }
}