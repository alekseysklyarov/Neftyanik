IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

CREATE TABLE [AspNetRoles] (
    [Id] nvarchar(450) NOT NULL,
    [Name] nvarchar(256) NULL,
    [NormalizedName] nvarchar(256) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [AspNetUsers] (
    [Id] nvarchar(450) NOT NULL,
    [FirstName] nvarchar(100) NOT NULL,
    [LastName] nvarchar(100) NOT NULL,
    [MiddleName] nvarchar(100) NULL,
    [IsActive] bit NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [LastLoginAt] datetimeoffset NULL,
    [DisplayName] nvarchar(200) NULL,
    [UserName] nvarchar(256) NULL,
    [NormalizedUserName] nvarchar(256) NULL,
    [Email] nvarchar(256) NULL,
    [NormalizedEmail] nvarchar(256) NULL,
    [EmailConfirmed] bit NOT NULL,
    [PasswordHash] nvarchar(max) NULL,
    [SecurityStamp] nvarchar(max) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    [PhoneNumber] nvarchar(max) NULL,
    [PhoneNumberConfirmed] bit NOT NULL,
    [TwoFactorEnabled] bit NOT NULL,
    [LockoutEnd] datetimeoffset NULL,
    [LockoutEnabled] bit NOT NULL,
    [AccessFailedCount] int NOT NULL,
    CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [ElectricityTariffs] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(200) NOT NULL,
    [SingleRatePrice] decimal(18,2) NOT NULL,
    [DayRatePrice] decimal(18,2) NULL,
    [NightRatePrice] decimal(18,2) NULL,
    [EffectiveFrom] date NOT NULL,
    [EffectiveTo] date NULL,
    [IsActive] bit NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_ElectricityTariffs] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [ExpenseCategories] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(200) NOT NULL,
    [Description] nvarchar(1000) NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_ExpenseCategories] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [MembershipFeeRates] (
    [Id] int NOT NULL IDENTITY,
    [Year] int NOT NULL,
    [AmountPerPlot] decimal(18,2) NOT NULL,
    [DueDate] date NULL,
    [IsActive] bit NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_MembershipFeeRates] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [AspNetRoleClaims] (
    [Id] int NOT NULL IDENTITY,
    [RoleId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [AspNetUserClaims] (
    [Id] int NOT NULL IDENTITY,
    [UserId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [AspNetUserLogins] (
    [LoginProvider] nvarchar(450) NOT NULL,
    [ProviderKey] nvarchar(450) NOT NULL,
    [ProviderDisplayName] nvarchar(max) NULL,
    [UserId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
    CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [AspNetUserRoles] (
    [UserId] nvarchar(450) NOT NULL,
    [RoleId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
    CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [AspNetUserTokens] (
    [UserId] nvarchar(450) NOT NULL,
    [LoginProvider] nvarchar(450) NOT NULL,
    [Name] nvarchar(450) NOT NULL,
    [Value] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
    CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [AssociationDocuments] (
    [Id] int NOT NULL IDENTITY,
    [Title] nvarchar(200) NOT NULL,
    [Description] nvarchar(1000) NULL,
    [DocumentType] int NOT NULL,
    [FilePath] nvarchar(500) NOT NULL,
    [OriginalFileName] nvarchar(260) NOT NULL,
    [IsPublic] bit NOT NULL,
    [PublishedAt] datetimeoffset NULL,
    [UploadedByUserId] nvarchar(450) NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_AssociationDocuments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AssociationDocuments_AspNetUsers_UploadedByUserId] FOREIGN KEY ([UploadedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [AuditLogs] (
    [Id] bigint NOT NULL IDENTITY,
    [UserId] nvarchar(450) NULL,
    [Action] nvarchar(200) NOT NULL,
    [EntityType] nvarchar(200) NOT NULL,
    [EntityId] nvarchar(100) NULL,
    [OldValues] nvarchar(max) NULL,
    [NewValues] nvarchar(max) NULL,
    [IpAddress] nvarchar(45) NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AuditLogs_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [ElectricityMeters] (
    [Id] int NOT NULL IDENTITY,
    [SerialNumber] nvarchar(100) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [MeterKind] int NOT NULL,
    [TariffMode] int NOT NULL,
    [IsActive] bit NOT NULL,
    [InstallationDate] date NULL,
    [InitialReading] decimal(18,3) NOT NULL,
    [InitialDayReading] decimal(18,3) NULL,
    [InitialNightReading] decimal(18,3) NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [OwnerId] nvarchar(450) NULL,
    CONSTRAINT [PK_ElectricityMeters] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ElectricityMeters_AspNetUsers_OwnerId] FOREIGN KEY ([OwnerId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [NewsArticles] (
    [Id] int NOT NULL IDENTITY,
    [Title] nvarchar(200) NOT NULL,
    [Summary] nvarchar(500) NULL,
    [Content] nvarchar(max) NOT NULL,
    [ImagePath] nvarchar(500) NULL,
    [IsPublished] bit NOT NULL,
    [IsPinned] bit NOT NULL,
    [PublishedAt] datetimeoffset NULL,
    [CreatedByUserId] nvarchar(450) NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [UpdatedAt] datetimeoffset NULL,
    CONSTRAINT [PK_NewsArticles] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_NewsArticles_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [Payments] (
    [Id] bigint NOT NULL IDENTITY,
    [UserId] nvarchar(450) NOT NULL,
    [PaymentDate] date NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [PaymentMethod] int NOT NULL,
    [ReferenceNumber] nvarchar(100) NULL,
    [Comment] nvarchar(1000) NULL,
    [CreatedByUserId] nvarchar(450) NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [IsCancelled] bit NOT NULL,
    [CancelledAt] datetimeoffset NULL,
    [CancelledByUserId] nvarchar(450) NULL,
    [Source] int NOT NULL,
    CONSTRAINT [PK_Payments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Payments_AspNetUsers_CancelledByUserId] FOREIGN KEY ([CancelledByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Payments_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Payments_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [Plots] (
    [Id] int NOT NULL IDENTITY,
    [Number] nvarchar(50) NOT NULL,
    [AreaSquareMeters] decimal(18,2) NULL,
    [Address] nvarchar(500) NULL,
    [OwnerId] nvarchar(450) NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [ClosedAt] datetimeoffset NULL,
    CONSTRAINT [PK_Plots] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Plots_AspNetUsers_OwnerId] FOREIGN KEY ([OwnerId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [SystemSettings] (
    [Id] int NOT NULL IDENTITY,
    [Key] nvarchar(200) NOT NULL,
    [Value] nvarchar(2000) NOT NULL,
    [Description] nvarchar(1000) NULL,
    [UpdatedAt] datetimeoffset NOT NULL,
    [UpdatedByUserId] nvarchar(450) NULL,
    CONSTRAINT [PK_SystemSettings] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SystemSettings_AspNetUsers_UpdatedByUserId] FOREIGN KEY ([UpdatedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [Expenses] (
    [Id] bigint NOT NULL IDENTITY,
    [ExpenseCategoryId] int NOT NULL,
    [ExpenseDate] date NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [Description] nvarchar(1000) NOT NULL,
    [Payee] nvarchar(200) NULL,
    [DocumentNumber] nvarchar(100) NULL,
    [AttachmentPath] nvarchar(500) NULL,
    [CreatedByUserId] nvarchar(450) NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [IsCancelled] bit NOT NULL,
    CONSTRAINT [PK_Expenses] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Expenses_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Expenses_ExpenseCategories_ExpenseCategoryId] FOREIGN KEY ([ExpenseCategoryId]) REFERENCES [ExpenseCategories] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [MeterReadings] (
    [Id] bigint NOT NULL IDENTITY,
    [MeterId] int NOT NULL,
    [ReadingDate] date NOT NULL,
    [TotalValue] decimal(18,3) NULL,
    [DayValue] decimal(18,3) NULL,
    [NightValue] decimal(18,3) NULL,
    [SubmittedByUserId] nvarchar(450) NULL,
    [Status] int NOT NULL,
    [SubmittedAt] datetimeoffset NOT NULL,
    [ApprovedByUserId] nvarchar(450) NULL,
    [ApprovedAt] datetimeoffset NULL,
    [Comment] nvarchar(1000) NULL,
    [MeterPhotoPath] nvarchar(500) NULL,
    CONSTRAINT [PK_MeterReadings] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_MeterReadings_AspNetUsers_ApprovedByUserId] FOREIGN KEY ([ApprovedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_MeterReadings_AspNetUsers_SubmittedByUserId] FOREIGN KEY ([SubmittedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_MeterReadings_ElectricityMeters_MeterId] FOREIGN KEY ([MeterId]) REFERENCES [ElectricityMeters] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [MeterPlots] (
    [MeterId] int NOT NULL,
    [PlotId] int NOT NULL,
    [ValidFrom] datetimeoffset NOT NULL,
    [ValidTo] datetimeoffset NULL,
    CONSTRAINT [PK_MeterPlots] PRIMARY KEY ([MeterId], [PlotId], [ValidFrom]),
    CONSTRAINT [FK_MeterPlots_ElectricityMeters_MeterId] FOREIGN KEY ([MeterId]) REFERENCES [ElectricityMeters] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_MeterPlots_Plots_PlotId] FOREIGN KEY ([PlotId]) REFERENCES [Plots] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [PlotOwnershipHistories] (
    [Id] bigint NOT NULL IDENTITY,
    [PlotId] int NOT NULL,
    [OwnerId] nvarchar(450) NOT NULL,
    [ValidFrom] datetimeoffset NOT NULL,
    [ValidTo] datetimeoffset NULL,
    [Comment] nvarchar(1000) NULL,
    CONSTRAINT [PK_PlotOwnershipHistories] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PlotOwnershipHistories_AspNetUsers_OwnerId] FOREIGN KEY ([OwnerId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PlotOwnershipHistories_Plots_PlotId] FOREIGN KEY ([PlotId]) REFERENCES [Plots] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [Charges] (
    [Id] bigint NOT NULL IDENTITY,
    [UserId] nvarchar(450) NOT NULL,
    [PlotId] int NULL,
    [MeterId] int NULL,
    [ChargeType] int NOT NULL,
    [PeriodYear] int NOT NULL,
    [PeriodMonth] int NULL,
    [Description] nvarchar(500) NOT NULL,
    [Quantity] decimal(18,3) NULL,
    [UnitPrice] decimal(18,2) NULL,
    [Amount] decimal(18,2) NOT NULL,
    [ChargedAt] datetimeoffset NOT NULL,
    [DueDate] date NULL,
    [Status] int NOT NULL,
    [SourceReadingId] bigint NULL,
    [CreatedByUserId] nvarchar(450) NULL,
    CONSTRAINT [PK_Charges] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Charges_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Charges_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Charges_ElectricityMeters_MeterId] FOREIGN KEY ([MeterId]) REFERENCES [ElectricityMeters] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Charges_MeterReadings_SourceReadingId] FOREIGN KEY ([SourceReadingId]) REFERENCES [MeterReadings] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Charges_Plots_PlotId] FOREIGN KEY ([PlotId]) REFERENCES [Plots] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [PaymentAllocations] (
    [Id] bigint NOT NULL IDENTITY,
    [PaymentId] bigint NOT NULL,
    [ChargeId] bigint NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_PaymentAllocations] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PaymentAllocations_Charges_ChargeId] FOREIGN KEY ([ChargeId]) REFERENCES [Charges] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PaymentAllocations_Payments_PaymentId] FOREIGN KEY ([PaymentId]) REFERENCES [Payments] ([Id]) ON DELETE NO ACTION
);
GO

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ConcurrencyStamp', N'Name', N'NormalizedName') AND [object_id] = OBJECT_ID(N'[AspNetRoles]'))
    SET IDENTITY_INSERT [AspNetRoles] ON;
INSERT INTO [AspNetRoles] ([Id], [ConcurrencyStamp], [Name], [NormalizedName])
VALUES (N'role-accountant', N'role-accountant', N'Accountant', N'ACCOUNTANT'),
(N'role-administrator', N'role-administrator', N'Administrator', N'ADMINISTRATOR'),
(N'role-member', N'role-member', N'Member', N'MEMBER');
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ConcurrencyStamp', N'Name', N'NormalizedName') AND [object_id] = OBJECT_ID(N'[AspNetRoles]'))
    SET IDENTITY_INSERT [AspNetRoles] OFF;
GO

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'CreatedAt', N'DayRatePrice', N'EffectiveFrom', N'EffectiveTo', N'IsActive', N'Name', N'NightRatePrice', N'SingleRatePrice') AND [object_id] = OBJECT_ID(N'[ElectricityTariffs]'))
    SET IDENTITY_INSERT [ElectricityTariffs] ON;
INSERT INTO [ElectricityTariffs] ([Id], [CreatedAt], [DayRatePrice], [EffectiveFrom], [EffectiveTo], [IsActive], [Name], [NightRatePrice], [SingleRatePrice])
VALUES (1, '2026-01-01T00:00:00.0000000+00:00', NULL, '2026-01-01', NULL, CAST(1 AS bit), N'Тариф 5,00 грн/кВт·ч', NULL, 5.0);
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'CreatedAt', N'DayRatePrice', N'EffectiveFrom', N'EffectiveTo', N'IsActive', N'Name', N'NightRatePrice', N'SingleRatePrice') AND [object_id] = OBJECT_ID(N'[ElectricityTariffs]'))
    SET IDENTITY_INSERT [ElectricityTariffs] OFF;
GO

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Description', N'IsActive', N'Name') AND [object_id] = OBJECT_ID(N'[ExpenseCategories]'))
    SET IDENTITY_INSERT [ExpenseCategories] ON;
INSERT INTO [ExpenseCategories] ([Id], [Description], [IsActive], [Name])
VALUES (1, NULL, CAST(1 AS bit), N'Оплата электроэнергии'),
(2, NULL, CAST(1 AS bit), N'Ремонт электросети'),
(3, NULL, CAST(1 AS bit), N'Ремонт дорог'),
(4, NULL, CAST(1 AS bit), N'Охрана'),
(5, NULL, CAST(1 AS bit), N'Вывоз мусора'),
(6, NULL, CAST(1 AS bit), N'Обслуживание территории'),
(7, NULL, CAST(1 AS bit), N'Административные расходы'),
(8, NULL, CAST(1 AS bit), N'Налоги и банковские комиссии'),
(9, NULL, CAST(1 AS bit), N'Прочее');
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Description', N'IsActive', N'Name') AND [object_id] = OBJECT_ID(N'[ExpenseCategories]'))
    SET IDENTITY_INSERT [ExpenseCategories] OFF;
GO

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AmountPerPlot', N'CreatedAt', N'DueDate', N'IsActive', N'Year') AND [object_id] = OBJECT_ID(N'[MembershipFeeRates]'))
    SET IDENTITY_INSERT [MembershipFeeRates] ON;
INSERT INTO [MembershipFeeRates] ([Id], [AmountPerPlot], [CreatedAt], [DueDate], [IsActive], [Year])
VALUES (1, 500.0, '2026-01-01T00:00:00.0000000+00:00', '2026-12-31', CAST(1 AS bit), 2026);
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AmountPerPlot', N'CreatedAt', N'DueDate', N'IsActive', N'Year') AND [object_id] = OBJECT_ID(N'[MembershipFeeRates]'))
    SET IDENTITY_INSERT [MembershipFeeRates] OFF;
GO

CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);
GO

CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL;
GO

CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
GO

CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);
GO

CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);
GO

CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);
GO

CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL;
GO

CREATE INDEX [IX_AssociationDocuments_UploadedByUserId] ON [AssociationDocuments] ([UploadedByUserId]);
GO

CREATE INDEX [IX_AuditLogs_EntityType_EntityId] ON [AuditLogs] ([EntityType], [EntityId]);
GO

CREATE INDEX [IX_AuditLogs_UserId] ON [AuditLogs] ([UserId]);
GO

CREATE INDEX [IX_Charges_CreatedByUserId] ON [Charges] ([CreatedByUserId]);
GO

CREATE INDEX [IX_Charges_MeterId] ON [Charges] ([MeterId]);
GO

CREATE INDEX [IX_Charges_PlotId_PeriodYear_ChargeType] ON [Charges] ([PlotId], [PeriodYear], [ChargeType]);
GO

CREATE INDEX [IX_Charges_SourceReadingId] ON [Charges] ([SourceReadingId]);
GO

CREATE INDEX [IX_Charges_UserId_Status] ON [Charges] ([UserId], [Status]);
GO

CREATE INDEX [IX_ElectricityMeters_OwnerId] ON [ElectricityMeters] ([OwnerId]);
GO

CREATE UNIQUE INDEX [IX_ElectricityMeters_SerialNumber] ON [ElectricityMeters] ([SerialNumber]);
GO

CREATE INDEX [IX_ElectricityTariffs_EffectiveFrom] ON [ElectricityTariffs] ([EffectiveFrom]);
GO

CREATE INDEX [IX_Expenses_CreatedByUserId] ON [Expenses] ([CreatedByUserId]);
GO

CREATE INDEX [IX_Expenses_ExpenseCategoryId] ON [Expenses] ([ExpenseCategoryId]);
GO

CREATE INDEX [IX_Expenses_ExpenseDate] ON [Expenses] ([ExpenseDate]);
GO

CREATE UNIQUE INDEX [IX_MembershipFeeRates_Year] ON [MembershipFeeRates] ([Year]);
GO

CREATE INDEX [IX_MeterPlots_PlotId] ON [MeterPlots] ([PlotId]);
GO

CREATE INDEX [IX_MeterReadings_ApprovedByUserId] ON [MeterReadings] ([ApprovedByUserId]);
GO

CREATE UNIQUE INDEX [IX_MeterReadings_MeterId_ReadingDate] ON [MeterReadings] ([MeterId], [ReadingDate]);
GO

CREATE INDEX [IX_MeterReadings_SubmittedByUserId] ON [MeterReadings] ([SubmittedByUserId]);
GO

CREATE INDEX [IX_NewsArticles_CreatedByUserId] ON [NewsArticles] ([CreatedByUserId]);
GO

CREATE INDEX [IX_NewsArticles_IsPublished_PublishedAt] ON [NewsArticles] ([IsPublished], [PublishedAt]);
GO

CREATE INDEX [IX_PaymentAllocations_ChargeId] ON [PaymentAllocations] ([ChargeId]);
GO

CREATE INDEX [IX_PaymentAllocations_PaymentId] ON [PaymentAllocations] ([PaymentId]);
GO

CREATE INDEX [IX_Payments_CancelledByUserId] ON [Payments] ([CancelledByUserId]);
GO

CREATE INDEX [IX_Payments_CreatedByUserId] ON [Payments] ([CreatedByUserId]);
GO

CREATE INDEX [IX_Payments_UserId_PaymentDate] ON [Payments] ([UserId], [PaymentDate]);
GO

CREATE INDEX [IX_PlotOwnershipHistories_OwnerId] ON [PlotOwnershipHistories] ([OwnerId]);
GO

CREATE INDEX [IX_PlotOwnershipHistories_PlotId] ON [PlotOwnershipHistories] ([PlotId]);
GO

CREATE UNIQUE INDEX [IX_Plots_Number] ON [Plots] ([Number]);
GO

CREATE INDEX [IX_Plots_OwnerId] ON [Plots] ([OwnerId]);
GO

CREATE UNIQUE INDEX [IX_SystemSettings_Key] ON [SystemSettings] ([Key]);
GO

CREATE INDEX [IX_SystemSettings_UpdatedByUserId] ON [SystemSettings] ([UpdatedByUserId]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260727141638_InitialCreate', N'8.0.0');
GO

COMMIT;
GO

