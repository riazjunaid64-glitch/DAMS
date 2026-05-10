/*
  Fixes: SqlException — Invalid object name 'Employees'.

  Run this against the SAME database as your API connection string (e.g. DAMS_DB on JUNAID-RIAZ\SQLEXPRESS)
  when the __EFMigrationsHistory row exists but tables were never created or were dropped.

  Safe to run once: skips objects that already exist.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'[dbo].[Employees]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Employees] (
        [Id]            int NOT NULL IDENTITY(1,1),
        [FullName]      nvarchar(200) NOT NULL,
        [JobTitle]      nvarchar(100) NOT NULL,
        [Department]    nvarchar(100) NOT NULL,
        [Phone]         nvarchar(50) NOT NULL,
        [Email]         nvarchar(200) NULL,
        [Address]       nvarchar(500) NULL,
        [Salary]        decimal(18,2) NOT NULL,
        [JoinDate]      datetime2 NOT NULL,
        [Status]        int NOT NULL,
        [CreatedAt]     datetime2 NOT NULL,
        [UpdatedAt]     datetime2 NULL,
        CONSTRAINT [PK_Employees] PRIMARY KEY ([Id])
    );
END
GO

IF OBJECT_ID(N'[dbo].[EmployeeAttendances]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EmployeeAttendances] (
        [Id]            int NOT NULL IDENTITY(1,1),
        [EmployeeId]    int NOT NULL,
        [Date]          datetime2 NOT NULL,
        [Status]        int NOT NULL,
        [CheckInTime]   time NULL,
        [CheckOutTime]  time NULL,
        [Notes]         nvarchar(500) NULL,
        [CreatedAt]     datetime2 NOT NULL,
        CONSTRAINT [PK_EmployeeAttendances] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EmployeeAttendances_Employees_EmployeeId]
            FOREIGN KEY ([EmployeeId]) REFERENCES [dbo].[Employees]([Id]) ON DELETE CASCADE
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EmployeeAttendances_EmployeeId_Date' AND object_id = OBJECT_ID(N'dbo.EmployeeAttendances'))
BEGIN
    CREATE UNIQUE INDEX [IX_EmployeeAttendances_EmployeeId_Date]
        ON [dbo].[EmployeeAttendances]([EmployeeId],[Date]);
END
GO

IF OBJECT_ID(N'[dbo].[EmployeeTasks]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EmployeeTasks] (
        [Id]            int NOT NULL IDENTITY(1,1),
        [EmployeeId]    int NOT NULL,
        [ProjectId]     int NULL,
        [Title]         nvarchar(200) NOT NULL,
        [Description]   nvarchar(1000) NULL,
        [Priority]      int NOT NULL,
        [Status]        int NOT NULL,
        [DueDate]       datetime2 NULL,
        [CompletedAt]   datetime2 NULL,
        [CreatedAt]     datetime2 NOT NULL,
        [UpdatedAt]     datetime2 NULL,
        CONSTRAINT [PK_EmployeeTasks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EmployeeTasks_Employees_EmployeeId]
            FOREIGN KEY ([EmployeeId]) REFERENCES [dbo].[Employees]([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_EmployeeTasks_Projects_ProjectId]
            FOREIGN KEY ([ProjectId]) REFERENCES [dbo].[Projects]([Id]) ON DELETE SET NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EmployeeTasks_EmployeeId' AND object_id = OBJECT_ID(N'dbo.EmployeeTasks'))
BEGIN
    CREATE INDEX [IX_EmployeeTasks_EmployeeId] ON [dbo].[EmployeeTasks]([EmployeeId]);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EmployeeTasks_ProjectId' AND object_id = OBJECT_ID(N'dbo.EmployeeTasks'))
BEGIN
    CREATE INDEX [IX_EmployeeTasks_ProjectId] ON [dbo].[EmployeeTasks]([ProjectId]);
END
GO

-- If Employees was created earlier with nvarchar(20) phone, widen to match the app model.
IF EXISTS (
    SELECT 1
    FROM sys.columns c
    WHERE c.object_id = OBJECT_ID(N'dbo.Employees')
      AND c.name = N'Phone'
      AND c.max_length = 40 -- nvarchar(20)
)
BEGIN
    ALTER TABLE [dbo].[Employees] ALTER COLUMN [Phone] nvarchar(50) NOT NULL;
END
GO
