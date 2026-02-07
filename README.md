# DbMetaTool
## <b>Opis projektu</b>

DbMetaTool to aplikacja konsolowa służąca do zarządzania metadanymi baz danych Firebird 5.0.
Pozwala na:
- budowanie nowej bazy danych na podstawie skryptów SQL
- eksport metadanych z istniejącej bazy do plików SQL
- częściową aktualizację bazy danych na podstawie skryptów

Aplikacja obsługuje tylko domeny, tabele oraz procedury.

## <b>Wymagania</b>
- .NET 8
- Firebird Server 5.0
- Pakiet NuGet: FirebirdSql.Data.FirebirdClient

## <b>Uruchomienie</b>
Aplikacja działa jako narzędzie konsolowe z następującymi komendami:
```
build-db --db-dir <ścieżka> --scripts-dir <ścieżka>
export-scripts --connection-string <connStr> --output-dir <ścieżka>
update-db --connection-string <connStr> --scripts-dir <ścieżka>
```

Parametry:
```--db-dir``` - katalog, w którym zostanie utworzona baza danych
```--scripts-dir``` - katalog ze skryptami SQL
```--connection-string``` - connection string do istniejącej bazy
```--output-dir``` - katalog zapisu wygenerowanych plików

Komendy:
<b>build-db</b> 
```
  DbMetaTool.exe build-db 
  --db-dir "C:\...\" 
  --scripts-dir "C:\...\"
  ```

<b>export-scripts</b>
```
  DbMetaTool.exe export-scripts 
  --connection-string "user id=SYSDBA;password=masterkey;data source=localhost;port number=3050;initial catalog=C:\...\new_database.fdb;dialect=3;character set=UTF8" 
  --output-dir "C:\...\"
```

<b>update-db</b>
```
  DbMetaTool.exe update-db 
  --connection-string "user id=SYSDBA;password=masterkey;data source=localhost;port number=3050;initial catalog=C:\...\new_database.fdb;dialect=3;character set=UTF8" 
  --scripts-dir "C:\...\"
```
⚠ Komenda wymaga poprawy - aktualizacja nie działa różnicowo - komendy które wykonają się poprawnie, zostaną zastosowane, komendy które zakończą się błędem, nie zostaną wykonane.

## <b>❗ Ograniczenia i uwagi </b>
Skrypty nie powinny zawierać ```SET TERM```, poleceń tworzenia bazy danych i poleceń łączenia z bazą danych. Powinny zawierać wyłącznie DDL obiektów.
Dodatkowo:
- Skrypty są parsowane przed wykonaniem
- Ignorowane są komentarze blokowe
- Usuwane są polecenia SET SQL DIALECT

