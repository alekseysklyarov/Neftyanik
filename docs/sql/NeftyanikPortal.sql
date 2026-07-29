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
    [MustChangePassword] bit NOT NULL DEFAULT CAST(0 AS bit),
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

CREATE TABLE [ChargeTypes] (
    [Id] int NOT NULL IDENTITY,
    [Code] varchar(64) NULL,
    [Name] nvarchar(150) NOT NULL,
    [Description] nvarchar(1000) NULL,
    [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    [IsDefault] bit NOT NULL DEFAULT CAST(0 AS bit),
    [IsYearly] bit NOT NULL DEFAULT CAST(0 AS bit),
    [OnlyOnOwnerChange] bit NOT NULL DEFAULT CAST(0 AS bit),
    [DefaultAmount] decimal(18,2) NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [UpdatedAtUtc] datetime2 NULL,
    CONSTRAINT [PK_ChargeTypes] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_ChargeTypes_DefaultAmount_Positive] CHECK ([DefaultAmount] IS NULL OR [DefaultAmount] > 0),
    CONSTRAINT [CK_ChargeTypes_YearlyAndOwnerChangeExclusive] CHECK ([IsYearly] = 0 OR [OnlyOnOwnerChange] = 0)
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

CREATE TABLE [Plots] (
    [Id] int NOT NULL IDENTITY,
    [Number] nvarchar(50) NOT NULL,
    [Address] nvarchar(250) NULL,
    [AreaSquareMeters] decimal(18,2) NULL,
    [CadastralNumber] nvarchar(100) NULL,
    [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    [Notes] nvarchar(2000) NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [UpdatedAtUtc] datetime2 NULL,
    CONSTRAINT [PK_Plots] PRIMARY KEY ([Id])
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

CREATE TABLE [AssociationElectricityReadings] (
    [Id] bigint NOT NULL IDENTITY,
    [ReadingDate] date NOT NULL,
    [PreviousDayReading] decimal(18,3) NULL,
    [CurrentDayReading] decimal(18,3) NOT NULL,
    [DayConsumption] decimal(18,3) NULL,
    [AppliedSupplierDayRate] decimal(18,4) NULL,
    [DayAmount] decimal(18,2) NULL,
    [PreviousNightReading] decimal(18,3) NULL,
    [CurrentNightReading] decimal(18,3) NOT NULL,
    [NightConsumption] decimal(18,3) NULL,
    [AppliedSupplierNightRate] decimal(18,4) NULL,
    [NightAmount] decimal(18,2) NULL,
    [TotalConsumption] decimal(18,3) NULL,
    [TotalSupplierAmount] decimal(18,2) NULL,
    [IsInitialReading] bit NOT NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [CreatedByUserId] nvarchar(450) NULL,
    CONSTRAINT [PK_AssociationElectricityReadings] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_AssociationElectricityReadings_CurrentDayReading_NonNegative] CHECK ([CurrentDayReading] >= 0),
    CONSTRAINT [CK_AssociationElectricityReadings_CurrentNightReading_NonNegative] CHECK ([CurrentNightReading] >= 0),
    CONSTRAINT [CK_AssociationElectricityReadings_DayAmount_NonNegative] CHECK ([DayAmount] IS NULL OR [DayAmount] >= 0),
    CONSTRAINT [CK_AssociationElectricityReadings_DayConsumption_NonNegative] CHECK ([DayConsumption] IS NULL OR [DayConsumption] >= 0),
    CONSTRAINT [CK_AssociationElectricityReadings_NightAmount_NonNegative] CHECK ([NightAmount] IS NULL OR [NightAmount] >= 0),
    CONSTRAINT [CK_AssociationElectricityReadings_NightConsumption_NonNegative] CHECK ([NightConsumption] IS NULL OR [NightConsumption] >= 0),
    CONSTRAINT [CK_AssociationElectricityReadings_PreviousDayReading_NonNegative] CHECK ([PreviousDayReading] IS NULL OR [PreviousDayReading] >= 0),
    CONSTRAINT [CK_AssociationElectricityReadings_PreviousNightReading_NonNegative] CHECK ([PreviousNightReading] IS NULL OR [PreviousNightReading] >= 0),
    CONSTRAINT [CK_AssociationElectricityReadings_TotalConsumption_NonNegative] CHECK ([TotalConsumption] IS NULL OR [TotalConsumption] >= 0),
    CONSTRAINT [CK_AssociationElectricityReadings_TotalSupplierAmount_NonNegative] CHECK ([TotalSupplierAmount] IS NULL OR [TotalSupplierAmount] >= 0),
    CONSTRAINT [FK_AssociationElectricityReadings_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [AssociationElectricityTariffs] (
    [Id] int NOT NULL IDENTITY,
    [EffectiveFrom] date NOT NULL,
    [DayRate] decimal(18,4) NOT NULL,
    [NightRate] decimal(18,4) NOT NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [CreatedByUserId] nvarchar(450) NULL,
    CONSTRAINT [PK_AssociationElectricityTariffs] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_AssociationElectricityTariffs_DayRate_NonNegative] CHECK ([DayRate] >= 0),
    CONSTRAINT [CK_AssociationElectricityTariffs_NightRate_NonNegative] CHECK ([NightRate] >= 0),
    CONSTRAINT [FK_AssociationElectricityTariffs_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
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

CREATE TABLE [MemberElectricityTariffs] (
    [Id] int NOT NULL IDENTITY,
    [EffectiveFrom] date NOT NULL,
    [Rate] decimal(18,4) NOT NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [CreatedByUserId] nvarchar(450) NULL,
    CONSTRAINT [PK_MemberElectricityTariffs] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_MemberElectricityTariffs_Rate_NonNegative] CHECK ([Rate] >= 0),
    CONSTRAINT [FK_MemberElectricityTariffs_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [Members] (
    [Id] int NOT NULL IDENTITY,
    [FullName] nvarchar(200) NOT NULL,
    [PhoneNumber] nvarchar(50) NULL,
    [Email] nvarchar(256) NULL,
    [ApplicationUserId] nvarchar(450) NULL,
    [JoinedAt] date NULL,
    [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    [Notes] nvarchar(2000) NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [UpdatedAtUtc] datetime2 NULL,
    CONSTRAINT [PK_Members] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Members_AspNetUsers_ApplicationUserId] FOREIGN KEY ([ApplicationUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE SET NULL
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

CREATE TABLE [Charges] (
    [Id] bigint NOT NULL IDENTITY,
    [PlotId] int NULL,
    [ChargeTypeId] int NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [ChargeDate] date NOT NULL,
    [DueDate] date NULL,
    [PeriodYear] int NULL,
    [PeriodMonth] int NULL,
    [Description] nvarchar(1000) NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CreatedByUserId] nvarchar(450) NULL,
    [CancelledAtUtc] datetime2 NULL,
    [CancellationReason] nvarchar(500) NULL,
    CONSTRAINT [PK_Charges] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Charges_Amount_Positive] CHECK ([Amount] >= 0),
    CONSTRAINT [CK_Charges_DueDate_NotEarlierThanChargeDate] CHECK ([DueDate] IS NULL OR [DueDate] >= [ChargeDate]),
    CONSTRAINT [CK_Charges_PeriodMonth_Range] CHECK ([PeriodMonth] IS NULL OR ([PeriodMonth] >= 1 AND [PeriodMonth] <= 12)),
    CONSTRAINT [CK_Charges_PeriodYear_Range] CHECK ([PeriodYear] IS NULL OR ([PeriodYear] >= 2000 AND [PeriodYear] <= 2100)),
    CONSTRAINT [FK_Charges_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Charges_ChargeTypes_ChargeTypeId] FOREIGN KEY ([ChargeTypeId]) REFERENCES [ChargeTypes] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Charges_Plots_PlotId] FOREIGN KEY ([PlotId]) REFERENCES [Plots] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [Payments] (
    [Id] bigint NOT NULL IDENTITY,
    [PlotId] int NULL,
    [PaymentDate] date NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [PaymentMethod] int NOT NULL,
    [ReferenceNumber] nvarchar(150) NULL,
    [Description] nvarchar(1000) NULL,
    [CreatedByUserId] nvarchar(450) NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CancelledAtUtc] datetime2 NULL,
    [CancellationReason] nvarchar(500) NULL,
    CONSTRAINT [PK_Payments] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Payments_Amount_Positive] CHECK ([Amount] > 0),
    CONSTRAINT [FK_Payments_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Payments_Plots_PlotId] FOREIGN KEY ([PlotId]) REFERENCES [Plots] ([Id]) ON DELETE NO ACTION
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
    [CancellationReason] nvarchar(500) NULL,
    [CancelledAt] datetimeoffset NULL,
    [UpdatedAt] datetimeoffset NULL,
    [AssociationElectricityReadingId] bigint NULL,
    CONSTRAINT [PK_Expenses] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Expenses_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Expenses_AssociationElectricityReadings_AssociationElectricityReadingId] FOREIGN KEY ([AssociationElectricityReadingId]) REFERENCES [AssociationElectricityReadings] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Expenses_ExpenseCategories_ExpenseCategoryId] FOREIGN KEY ([ExpenseCategoryId]) REFERENCES [ExpenseCategories] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [MemberElectricityMeters] (
    [Id] int NOT NULL IDENTITY,
    [MemberId] int NOT NULL,
    [MeterNumber] nvarchar(100) NULL,
    [Name] nvarchar(200) NULL,
    [IsActive] bit NOT NULL,
    [BillingPlotId] int NOT NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [CreatedByUserId] nvarchar(450) NULL,
    CONSTRAINT [PK_MemberElectricityMeters] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_MemberElectricityMeters_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_MemberElectricityMeters_Members_MemberId] FOREIGN KEY ([MemberId]) REFERENCES [Members] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_MemberElectricityMeters_Plots_BillingPlotId] FOREIGN KEY ([BillingPlotId]) REFERENCES [Plots] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [PlotOwnerships] (
    [Id] int NOT NULL IDENTITY,
    [PlotId] int NOT NULL,
    [MemberId] int NOT NULL,
    [OwnershipShare] decimal(5,2) NULL,
    [IsPrimaryContact] bit NOT NULL,
    [ValidFrom] date NULL,
    [ValidTo] date NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_PlotOwnerships] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_PlotOwnerships_OwnershipShare_Range] CHECK ([OwnershipShare] IS NULL OR ([OwnershipShare] > 0 AND [OwnershipShare] <= 100)),
    CONSTRAINT [CK_PlotOwnerships_ValidTo_NotEarlierThanValidFrom] CHECK ([ValidFrom] IS NULL OR [ValidTo] IS NULL OR [ValidTo] >= [ValidFrom]),
    CONSTRAINT [FK_PlotOwnerships_Members_MemberId] FOREIGN KEY ([MemberId]) REFERENCES [Members] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PlotOwnerships_Plots_PlotId] FOREIGN KEY ([PlotId]) REFERENCES [Plots] ([Id]) ON DELETE NO ACTION
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

CREATE TABLE [MemberElectricityMeterPlots] (
    [MemberElectricityMeterId] int NOT NULL,
    [PlotId] int NOT NULL,
    CONSTRAINT [PK_MemberElectricityMeterPlots] PRIMARY KEY ([MemberElectricityMeterId], [PlotId]),
    CONSTRAINT [FK_MemberElectricityMeterPlots_MemberElectricityMeters_MemberElectricityMeterId] FOREIGN KEY ([MemberElectricityMeterId]) REFERENCES [MemberElectricityMeters] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_MemberElectricityMeterPlots_Plots_PlotId] FOREIGN KEY ([PlotId]) REFERENCES [Plots] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [MemberElectricityReadings] (
    [Id] bigint NOT NULL IDENTITY,
    [MemberElectricityMeterId] int NOT NULL,
    [ReadingDate] date NOT NULL,
    [PreviousReading] decimal(18,3) NULL,
    [CurrentReading] decimal(18,3) NOT NULL,
    [Consumption] decimal(18,3) NULL,
    [AppliedMemberRate] decimal(18,4) NULL,
    [Amount] decimal(18,2) NULL,
    [IsInitialReading] bit NOT NULL,
    [ChargeId] bigint NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [CreatedByUserId] nvarchar(450) NULL,
    [SubmittedByMember] bit NOT NULL,
    CONSTRAINT [PK_MemberElectricityReadings] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_MemberElectricityReadings_Amount_NonNegative] CHECK ([Amount] IS NULL OR [Amount] >= 0),
    CONSTRAINT [CK_MemberElectricityReadings_AppliedMemberRate_NonNegative] CHECK ([AppliedMemberRate] IS NULL OR [AppliedMemberRate] >= 0),
    CONSTRAINT [CK_MemberElectricityReadings_Consumption_NonNegative] CHECK ([Consumption] IS NULL OR [Consumption] >= 0),
    CONSTRAINT [CK_MemberElectricityReadings_CurrentReading_NonNegative] CHECK ([CurrentReading] >= 0),
    CONSTRAINT [CK_MemberElectricityReadings_PreviousReading_NonNegative] CHECK ([PreviousReading] IS NULL OR [PreviousReading] >= 0),
    CONSTRAINT [FK_MemberElectricityReadings_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_MemberElectricityReadings_Charges_ChargeId] FOREIGN KEY ([ChargeId]) REFERENCES [Charges] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_MemberElectricityReadings_MemberElectricityMeters_MemberElectricityMeterId] FOREIGN KEY ([MemberElectricityMeterId]) REFERENCES [MemberElectricityMeters] ([Id]) ON DELETE NO ACTION
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

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Description', N'IsActive', N'Name') AND [object_id] = OBJECT_ID(N'[ExpenseCategories]'))
    SET IDENTITY_INSERT [ExpenseCategories] ON;
INSERT INTO [ExpenseCategories] ([Id], [Description], [IsActive], [Name])
VALUES (1, NULL, CAST(1 AS bit), N'Электроэнергия'),
(2, NULL, CAST(1 AS bit), N'Охрана'),
(3, NULL, CAST(1 AS bit), N'Зарплата бухгалтеру и председателю'),
(4, NULL, CAST(1 AS bit), N'Покупка нового имущества для кооператива'),
(5, NULL, CAST(1 AS bit), N'Ремонт имущества для кооператива'),
(6, NULL, CAST(1 AS bit), N'Наемный труд для кооператива'),
(7, NULL, CAST(0 AS bit), N'Административные расходы'),
(8, NULL, CAST(0 AS bit), N'Налоги и банковские комиссии'),
(9, NULL, CAST(0 AS bit), N'Прочее');
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

CREATE INDEX [IX_AssociationElectricityReadings_CreatedByUserId] ON [AssociationElectricityReadings] ([CreatedByUserId]);
GO

CREATE UNIQUE INDEX [IX_AssociationElectricityReadings_IsInitialReading] ON [AssociationElectricityReadings] ([IsInitialReading]) WHERE [IsInitialReading] = 1;
GO

CREATE UNIQUE INDEX [IX_AssociationElectricityReadings_ReadingDate] ON [AssociationElectricityReadings] ([ReadingDate]);
GO

CREATE INDEX [IX_AssociationElectricityTariffs_CreatedByUserId] ON [AssociationElectricityTariffs] ([CreatedByUserId]);
GO

CREATE UNIQUE INDEX [IX_AssociationElectricityTariffs_EffectiveFrom] ON [AssociationElectricityTariffs] ([EffectiveFrom]);
GO

CREATE INDEX [IX_AuditLogs_EntityType_EntityId] ON [AuditLogs] ([EntityType], [EntityId]);
GO

CREATE INDEX [IX_AuditLogs_UserId] ON [AuditLogs] ([UserId]);
GO

CREATE INDEX [IX_Charges_CancelledAtUtc] ON [Charges] ([CancelledAtUtc]);
GO

CREATE INDEX [IX_Charges_ChargeDate] ON [Charges] ([ChargeDate]);
GO

CREATE INDEX [IX_Charges_ChargeTypeId] ON [Charges] ([ChargeTypeId]);
GO

CREATE INDEX [IX_Charges_CreatedByUserId] ON [Charges] ([CreatedByUserId]);
GO

CREATE INDEX [IX_Charges_DueDate] ON [Charges] ([DueDate]);
GO

CREATE INDEX [IX_Charges_PlotId] ON [Charges] ([PlotId]);
GO

CREATE UNIQUE INDEX [IX_ChargeTypes_Code] ON [ChargeTypes] ([Code]) WHERE [Code] IS NOT NULL;
GO

CREATE UNIQUE INDEX [IX_ChargeTypes_IsDefault] ON [ChargeTypes] ([IsDefault]) WHERE [IsDefault] = 1 AND [IsActive] = 1;
GO

CREATE INDEX [IX_ChargeTypes_Name] ON [ChargeTypes] ([Name]);
GO

CREATE UNIQUE INDEX [IX_Expenses_AssociationElectricityReadingId] ON [Expenses] ([AssociationElectricityReadingId]) WHERE [AssociationElectricityReadingId] IS NOT NULL;
GO

CREATE INDEX [IX_Expenses_CreatedByUserId] ON [Expenses] ([CreatedByUserId]);
GO

CREATE INDEX [IX_Expenses_ExpenseCategoryId] ON [Expenses] ([ExpenseCategoryId]);
GO

CREATE INDEX [IX_Expenses_ExpenseDate] ON [Expenses] ([ExpenseDate]);
GO

CREATE INDEX [IX_MemberElectricityMeterPlots_PlotId] ON [MemberElectricityMeterPlots] ([PlotId]);
GO

CREATE INDEX [IX_MemberElectricityMeters_BillingPlotId] ON [MemberElectricityMeters] ([BillingPlotId]);
GO

CREATE INDEX [IX_MemberElectricityMeters_CreatedByUserId] ON [MemberElectricityMeters] ([CreatedByUserId]);
GO

CREATE INDEX [IX_MemberElectricityMeters_MemberId] ON [MemberElectricityMeters] ([MemberId]);
GO

CREATE UNIQUE INDEX [IX_MemberElectricityReadings_ChargeId] ON [MemberElectricityReadings] ([ChargeId]) WHERE [ChargeId] IS NOT NULL;
GO

CREATE INDEX [IX_MemberElectricityReadings_CreatedByUserId] ON [MemberElectricityReadings] ([CreatedByUserId]);
GO

CREATE UNIQUE INDEX [IX_MemberElectricityReadings_MemberElectricityMeterId_IsInitialReading] ON [MemberElectricityReadings] ([MemberElectricityMeterId], [IsInitialReading]) WHERE [IsInitialReading] = 1;
GO

CREATE UNIQUE INDEX [IX_MemberElectricityReadings_MemberElectricityMeterId_ReadingDate] ON [MemberElectricityReadings] ([MemberElectricityMeterId], [ReadingDate]);
GO

CREATE INDEX [IX_MemberElectricityTariffs_CreatedByUserId] ON [MemberElectricityTariffs] ([CreatedByUserId]);
GO

CREATE UNIQUE INDEX [IX_MemberElectricityTariffs_EffectiveFrom] ON [MemberElectricityTariffs] ([EffectiveFrom]);
GO

CREATE INDEX [IX_Members_ApplicationUserId] ON [Members] ([ApplicationUserId]);
GO

CREATE INDEX [IX_Members_Email] ON [Members] ([Email]);
GO

CREATE INDEX [IX_Members_FullName] ON [Members] ([FullName]);
GO

CREATE UNIQUE INDEX [IX_MembershipFeeRates_Year] ON [MembershipFeeRates] ([Year]);
GO

CREATE INDEX [IX_NewsArticles_CreatedByUserId] ON [NewsArticles] ([CreatedByUserId]);
GO

CREATE INDEX [IX_NewsArticles_IsPublished_PublishedAt] ON [NewsArticles] ([IsPublished], [PublishedAt]);
GO

CREATE INDEX [IX_PaymentAllocations_ChargeId] ON [PaymentAllocations] ([ChargeId]);
GO

CREATE INDEX [IX_PaymentAllocations_PaymentId] ON [PaymentAllocations] ([PaymentId]);
GO

CREATE INDEX [IX_Payments_CancelledAtUtc] ON [Payments] ([CancelledAtUtc]);
GO

CREATE INDEX [IX_Payments_CreatedByUserId] ON [Payments] ([CreatedByUserId]);
GO

CREATE INDEX [IX_Payments_PaymentDate] ON [Payments] ([PaymentDate]);
GO

CREATE INDEX [IX_Payments_PlotId] ON [Payments] ([PlotId]);
GO

CREATE INDEX [IX_Payments_ReferenceNumber] ON [Payments] ([ReferenceNumber]);
GO

CREATE INDEX [IX_PlotOwnershipHistories_OwnerId] ON [PlotOwnershipHistories] ([OwnerId]);
GO

CREATE INDEX [IX_PlotOwnershipHistories_PlotId] ON [PlotOwnershipHistories] ([PlotId]);
GO

CREATE INDEX [IX_PlotOwnerships_MemberId] ON [PlotOwnerships] ([MemberId]);
GO

CREATE UNIQUE INDEX [IX_PlotOwnerships_PlotId] ON [PlotOwnerships] ([PlotId]) WHERE [ValidTo] IS NULL;
GO

CREATE INDEX [IX_Plots_CadastralNumber] ON [Plots] ([CadastralNumber]);
GO

CREATE UNIQUE INDEX [IX_Plots_Number] ON [Plots] ([Number]);
GO

CREATE UNIQUE INDEX [IX_SystemSettings_Key] ON [SystemSettings] ([Key]);
GO

CREATE INDEX [IX_SystemSettings_UpdatedByUserId] ON [SystemSettings] ([UpdatedByUserId]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260729185604_InitialCleanSchema', N'8.0.0');
GO

COMMIT;
GO

