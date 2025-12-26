CREATE USER [js-DemoDBusers] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [js-DemoDBusers];
ALTER ROLE db_datawriter ADD MEMBER [js-DemoDBusers];