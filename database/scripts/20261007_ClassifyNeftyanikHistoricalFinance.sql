-- One-time, repeatable correction of confirmed historical classifications.
-- User confirmation: annual fee = membership fee; expenses 3-6 funded by fees;
-- Latest user instruction: all test expenses paid in cash (including 1,2,7).
-- No amounts, dates, charge debtors, payments or allocations are changed.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
IF DB_NAME() <> N'NeftyanikRestoreTest'
    THROW 51000, 'Unexpected database; this script is scoped to NeftyanikRestoreTest.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock @Resource=N'DachaHub.PaymentAllocation:1',
        @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000;
    IF @lockResult < 0 THROW 51001, 'Could not acquire finance lock.', 1;
    IF NOT EXISTS (SELECT 1 FROM Associations WHERE Id=1 AND Slug=N'neftyanik')
        THROW 51002, 'Unexpected association.', 1;
    IF NOT EXISTS (SELECT 1 FROM ChargeTypes WITH (UPDLOCK,HOLDLOCK)
        WHERE AssociationId=1 AND Id=2 AND DefaultAmount=500 AND IsYearly=1
        AND OnlyOnOwnerChange=0 AND Name=N'Ежегодный платеж в кассу дачного товарищества 2026 г')
        THROW 51003, 'Membership fee no longer matches the reviewed record.', 1;

    DECLARE @expected TABLE (Id bigint PRIMARY KEY, Amount decimal(18,2), ExpenseDate date,
        ReadingId bigint NULL, PaymentMethod int, FundingSource int);
    INSERT INTO @expected VALUES
        (1,10888.73,'20260701',2,1,2),
        (2,7288.19,'20260801',3,1,2),
        (3,1000.00,'20260814',NULL,1,1),
        (4,3200.00,'20260815',NULL,1,1),
        (5,8000.00,'20260815',NULL,1,1),
        (6,600.00,'20260815',NULL,1,1),
        (7,9163.07,'20260903',4,1,2);
    IF (SELECT COUNT(*) FROM @expected x JOIN Expenses e WITH (UPDLOCK,HOLDLOCK)
        ON e.AssociationId=1 AND e.Id=x.Id AND e.Amount=x.Amount AND e.ExpenseDate=x.ExpenseDate
        AND ISNULL(e.AssociationElectricityReadingId,-1)=ISNULL(x.ReadingId,-1)
        AND e.IsCancelled=0 AND e.PaymentMethod IN (1,x.PaymentMethod)
        AND e.FundingSource IN (0,x.FundingSource)) <> 7
        THROW 51004, 'Expenses no longer match the reviewed records.', 1;

    DECLARE @now datetime2=SYSUTCDATETIME();
    DECLARE @feeChanges TABLE (Id int, OldFlag bit, NewFlag bit);
    UPDATE ChargeTypes SET IsMembershipFee=1, UpdatedAtUtc=@now
    OUTPUT inserted.Id,deleted.IsMembershipFee,inserted.IsMembershipFee INTO @feeChanges
    WHERE AssociationId=1 AND Id=2 AND IsMembershipFee=0;
    INSERT INTO FinancialAuditLogs
        (AssociationId,CreatedAtUtc,UserId,UserName,Action,EntityType,EntityId,Description,OldValuesJson,NewValuesJson)
    SELECT 1,@now,NULL,N'Codex: подтверждено пользователем',N'Updated',N'ChargeType',CONVERT(nvarchar(100),f.Id),
        N'По подтверждению пользователя ежегодный платёж 500 грн отмечен как членский взнос.',
        (SELECT f.OldFlag AS IsMembershipFee FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
        (SELECT f.NewFlag AS IsMembershipFee FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
    FROM @feeChanges f;

    DECLARE @expenseChanges TABLE (Id bigint, OldMethod int, NewMethod int, OldFund int, NewFund int);
    UPDATE e SET PaymentMethod=x.PaymentMethod, FundingSource=x.FundingSource, UpdatedAt=@now
    OUTPUT inserted.Id,deleted.PaymentMethod,inserted.PaymentMethod,deleted.FundingSource,inserted.FundingSource
        INTO @expenseChanges
    FROM Expenses e JOIN @expected x ON x.Id=e.Id
    WHERE e.AssociationId=1 AND (e.PaymentMethod<>x.PaymentMethod OR e.FundingSource<>x.FundingSource);
    INSERT INTO FinancialAuditLogs
        (AssociationId,CreatedAtUtc,UserId,UserName,Action,EntityType,EntityId,Description,OldValuesJson,NewValuesJson)
    SELECT 1,@now,NULL,N'Codex: подтверждено пользователем',N'Updated',N'Expense',CONVERT(nvarchar(100),e.Id),
        N'По подтверждению пользователя уточнены счёт оплаты и источник средств исторического расхода. Сумма и дата сохранены.',
        (SELECT e.OldMethod AS PaymentMethod,e.OldFund AS FundingSource FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
        (SELECT e.NewMethod AS PaymentMethod,e.NewFund AS FundingSource FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
    FROM @expenseChanges e;
    COMMIT TRANSACTION;
    SELECT (SELECT COUNT(*) FROM @feeChanges) AS ChangedChargeTypes,
        (SELECT COUNT(*) FROM @expenseChanges) AS ChangedExpenses;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
