-- Explicit user instruction: mark all neftyanik test expenses as cash.
-- Does not change application accounting logic, amounts, dates or funding sources.
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
    THROW 51000, 'Unexpected database; test database only.', 1;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock @Resource=N'DachaHub.PaymentAllocation:1',
        @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000;
    IF @lockResult < 0 THROW 51001, 'Could not acquire finance lock.', 1;
    IF NOT EXISTS (SELECT 1 FROM Associations WHERE Id=1 AND Slug=N'neftyanik')
        THROW 51002, 'Unexpected association.', 1;
    DECLARE @now datetime2=SYSUTCDATETIME();
    DECLARE @changes TABLE (Id bigint, OldMethod int, NewMethod int);
    UPDATE Expenses WITH (UPDLOCK,HOLDLOCK) SET PaymentMethod=1, UpdatedAt=@now
    OUTPUT inserted.Id,deleted.PaymentMethod,inserted.PaymentMethod INTO @changes
    WHERE AssociationId=1 AND PaymentMethod<>1;
    INSERT INTO FinancialAuditLogs
        (AssociationId,CreatedAtUtc,UserId,UserName,Action,EntityType,EntityId,Description,OldValuesJson,NewValuesJson)
    SELECT 1,@now,NULL,N'Codex: по указанию пользователя',N'Updated',N'Expense',CONVERT(nvarchar(100),e.Id),
        N'По указанию пользователя все расходы тестового товарищества отмечены как наличные. Суммы, даты и источники средств сохранены.',
        (SELECT e.OldMethod AS PaymentMethod FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
        (SELECT e.NewMethod AS PaymentMethod FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
    FROM @changes e;
    COMMIT TRANSACTION;
    SELECT COUNT(*) AS ChangedExpenses FROM @changes;
    SELECT PaymentMethod, COUNT(*) AS ExpenseCount, SUM(Amount) AS TotalAmount
    FROM Expenses WHERE AssociationId=1 GROUP BY PaymentMethod;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
