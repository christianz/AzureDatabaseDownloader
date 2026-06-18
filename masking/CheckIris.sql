/*
=====================================================================
 CheckIris PII anonymization — runs AFTER ImportBacpac, against the
 LOCAL (non-prod) database only. Never runs against Azure/production.

 Strategy:
   - UPDATE-in-place. Row counts and ALL foreign keys are preserved so
     existing tests still exercise the real graph; only PII *values* are
     overwritten with deterministic, reproducible fakes.
   - Identity columns (Id / CandidateId / OrderId / GUIDs) are NEVER touched.
   - Dense free-text / JSON result payloads are scrubbed (NULL or a tiny
     placeholder) because they embed unstructured PII (NIN, names, credit
     data) that can't be column-masked.

 Determinism: a candidate's name becomes "Testkandidat <n>" where <n> is a
 stable per-row number (ROW_NUMBER over Id), so the same row always maps to
 the same fake across refreshes.

 Review notes:
   - Columns that are config/template text (Activity*.Description*, Instructions,
     translations, RefQuestion.Text, etc.) are intentionally LEFT ALONE — they
     are product content, not customer PII.
   - Sections flagged [VERIFY] should be sanity-checked against current schema
     before first production use of the masked pipeline.
=====================================================================*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRAN;

-------------------------------------------------------------------------------
-- 1. CANDIDATES  (core data subject)
-------------------------------------------------------------------------------
;WITH c AS (
    SELECT Id, ROW_NUMBER() OVER (ORDER BY Id) AS rn FROM dbo.Candidate
)
UPDATE cand SET
    cand.Firstname  = N'Testkandidat',
    cand.Lastname   = N'Nr' + CAST(c.rn AS nvarchar(20)),
    cand.MaidenName = CASE WHEN cand.MaidenName IS NULL THEN NULL
                           ELSE N'Pikenavn' + CAST(c.rn AS nvarchar(20)) END,
    cand.Email      = 'candidate' + CAST(c.rn AS varchar(20)) + '@example.invalid',
    cand.Mobile     = RIGHT('00000000' + CAST((40000000 + c.rn) AS varchar(15)), 8)
FROM dbo.Candidate cand JOIN c ON c.Id = cand.Id;

-------------------------------------------------------------------------------
-- 2. APPLICATION USERS / PROFILES  (screeners, client users, admins)
--    Keep PasswordHash usable? No — null credentials; dev logs in via
--    /auto-login (Development-only, passwordless). Wipe secrets.
-------------------------------------------------------------------------------
;WITH u AS (
    SELECT Id, ROW_NUMBER() OVER (ORDER BY Id) AS rn FROM dbo.ApplicationUser
)
UPDATE au SET
    au.FullName            = N'Test User ' + CAST(u.rn AS nvarchar(20)),
    au.UserName            = 'user' + CAST(u.rn AS varchar(20)) + '@example.invalid',
    au.NormalizedUserName  = 'USER' + CAST(u.rn AS varchar(20)) + '@EXAMPLE.INVALID',
    -- NB: this Identity schema has no Email/NormalizedEmail column; the login email lives in UserName (masked above).
    au.PhoneNumber         = '47' + RIGHT('00000000' + CAST((40000000 + u.rn) AS varchar(20)), 8),
    au.PasswordHash        = NULL,
    au.SecurityStamp       = NULL,
    au.AuthenticatorKey    = NULL
FROM dbo.ApplicationUser au JOIN u ON u.Id = au.Id;

UPDATE dbo.UserProfile SET
    FirstName   = N'Test',
    LastName    = N'User',
    DateOfBirth = CASE WHEN DateOfBirth IS NULL THEN NULL ELSE '1980-01-01' END;

-------------------------------------------------------------------------------
-- 3. DENSE RESULT / PAYLOAD COLUMNS  (highest PII density — scrub hard)
-------------------------------------------------------------------------------
UPDATE dbo.CreditReport          SET RawJson = NULL, AuditorName = N'Revisor';
UPDATE dbo.ActivityResult        SET JsonData = NULL, JsonDataOriginal = NULL;
UPDATE dbo.ActivityRawPayloads   SET Payload = NULL;
UPDATE dbo.CandidateCV           SET ParsedData = NULL;
UPDATE dbo.OrderActivityInput    SET JsonData = NULL;
UPDATE dbo.PickOrderActivityInput SET JsonData = NULL;
UPDATE dbo.Company               SET RawJson = NULL;                 -- registry pull (business data, but may contain director PII)
UPDATE dbo.SignatureRecord       SET SignatureEvidence = NULL, SubjectNin = NULL,
                                     SubjectFirstName = N'Test', SubjectLastName = N'Signer',
                                     SubjectEmail = 'signer@example.invalid';
UPDATE dbo.SignicatEvent         SET RawBody = NULL, ProcessingError = NULL;
UPDATE dbo.InboundEmailMessage   SET RawHeadersJson = NULL, FromAddress = 'inbound@example.invalid';
UPDATE dbo.SourceVerificationRequest SET CVEntryData = NULL, ResponseComment = NULL,
                                         ManualSourceName = N'Kilde', CandidateBirthDate = NULL;

-------------------------------------------------------------------------------
-- 4. REFERENCE-CHECK DATA  (free-text statements about the data subject)
-------------------------------------------------------------------------------
;WITH r AS (
    SELECT Id, ROW_NUMBER() OVER (ORDER BY Id) AS rn FROM dbo.RefOrderReference
)
UPDATE ror SET
    ror.Firstname        = N'Referanse',
    ror.Lastname         = N'Nr' + CAST(r.rn AS nvarchar(20)),
    ror.OrgName          = N'Testbedrift ' + CAST(r.rn AS nvarchar(20)),
    ror.ReferenceEmail   = 'reference' + CAST(r.rn AS varchar(20)) + '@example.invalid',
    ror.ReferenceMobile  = '47' + RIGHT('00000000' + CAST((40000000 + r.rn) AS varchar(14)), 8),
    ror.ReferenceComment = CASE WHEN ror.ReferenceComment IS NULL THEN NULL ELSE N'[anonymisert]' END,
    ror.IpAddress        = NULL,
    ror.SubmitIpAddress  = NULL
FROM dbo.RefOrderReference ror JOIN r ON r.Id = ror.Id;

UPDATE dbo.RefQuestionAnswer SET
    AnswerText = CASE WHEN AnswerText IS NULL THEN NULL ELSE N'[anonymisert svar]' END;
    -- QuestionText / OptionText left as-is: they are template text, not PII.

-------------------------------------------------------------------------------
-- 5. ORDER / CASEWORKER FREE-TEXT NOTES  (may quote candidate PII)
-------------------------------------------------------------------------------
UPDATE dbo.[Order] SET
    Comment              = CASE WHEN Comment IS NULL THEN NULL ELSE N'[anonymisert]' END,
    OverallResultComment = CASE WHEN OverallResultComment IS NULL THEN NULL ELSE N'[anonymisert]' END,
    PurchaseOrder        = CASE WHEN PurchaseOrder IS NULL THEN NULL ELSE N'PO-TEST' END;
UPDATE dbo.OrderActivity SET
    ClientComment  = CASE WHEN ClientComment  IS NULL THEN NULL ELSE N'[anonymisert]' END,
    ConsentComment = CASE WHEN ConsentComment IS NULL THEN NULL ELSE N'[anonymisert]' END;
UPDATE dbo.PickOrder SET
    Comment              = CASE WHEN Comment IS NULL THEN NULL ELSE N'[anonymisert]' END,
    OverallResultComment = CASE WHEN OverallResultComment IS NULL THEN NULL ELSE N'[anonymisert]' END;
UPDATE dbo.PickOrderActivity SET
    ConsentComment = CASE WHEN ConsentComment IS NULL THEN NULL ELSE N'[anonymisert]' END;

-------------------------------------------------------------------------------
-- 6. UPLOADED-DOCUMENT METADATA  (original filenames often = candidate name)
--    BlobUrl points at prod storage the dev box can't read anyway; null it
--    so nothing tries to fetch real candidate files.
-------------------------------------------------------------------------------
UPDATE dbo.OrderActivityDoc   SET OriginalName = N'document.pdf', DocName = N'document.pdf', BlobUrl = NULL;
UPDATE dbo.OrderDoc           SET OriginalName = N'document.pdf', DocName = N'document.pdf', BlobUrl = NULL;
UPDATE dbo.OrderDocRequestFile SET OriginalName = N'document.pdf', BlobUrl = NULL;
UPDATE dbo.SourceDoc          SET BlobUrl = NULL;
UPDATE dbo.Attachment         SET FileName = N'attachment' ;                       -- [VERIFY] keep extension if tests need it
UPDATE dbo.CompanyDocument    SET OriginalFileName = N'document.pdf';
UPDATE dbo.SecureUploadFile   SET OriginalFileName = N'document.pdf', UploaderIpAddress = NULL;
UPDATE dbo.LegacyVeriFindDocument SET FileName = N'document.pdf', SourceBlobName = NULL,
                                      UploadedByUserName = N'Test User';

-------------------------------------------------------------------------------
-- 7. THIRD-PARTY CONTACTS  (sources, signers, provider/company contacts)
-------------------------------------------------------------------------------
UPDATE dbo.SourceContact          SET Name = N'Kontaktperson', Email = 'source@example.invalid', Phone = '4740000000';
UPDATE dbo.InformationSourceEntity SET Name = N'Kilde', Email = 'source@example.invalid', Phone = '4740000000';
UPDATE dbo.Source                 SET NotificationEmail = 'source@example.invalid', NotificationMobile = '4740000000';
UPDATE dbo.DocumentSigner         SET Name = N'Signer', Email = 'signer@example.invalid', Phone = '4740000000';
UPDATE dbo.Director               SET Name = N'Styremedlem', Address = N'Testgata 1', DateOfBirth = NULL;
UPDATE dbo.Shareholder            SET Name = N'Aksjonær';
UPDATE dbo.ContactFormSubmission  SET Firstname = N'Test', Lastname = N'Person',
                                      Email = 'contact@example.invalid', Phone = '4740000000';
UPDATE dbo.SecureUploadLink       SET RecipientName = N'Mottaker', RecipientEmail = 'recipient@example.invalid',
                                      Message = CASE WHEN Message IS NULL THEN NULL ELSE N'[anonymisert]' END;
UPDATE dbo.SmsConfirmation        SET PhoneNumber = '4740000000';

-- Company contact people (the company name itself is business data; keep it,
-- but scrub the named contact person + direct contact channels).
UPDATE dbo.Company SET
    ContactFirstname = N'Kontakt', ContactLastname = N'Person',
    ContactEmail = 'contact@example.invalid', ContactMobile = '40000000',
    CompanyEmail = 'company@example.invalid', Telephone = '40000000';
UPDATE dbo.ScreeningProvider SET
    ContactFirstname = N'Kontakt', ContactLastname = N'Person',
    ContactEmail = 'contact@example.invalid', ContactMobile = '40000000',
    CompanyEmail = 'provider@example.invalid', Email = 'provider@example.invalid', Phone = '40000000';

-------------------------------------------------------------------------------
-- 8. ADDRESSES
-------------------------------------------------------------------------------
UPDATE dbo.Address SET Street = N'Testgata 1', City = N'Testby', Zip = N'0000';

-------------------------------------------------------------------------------
-- 9. COMMUNICATIONS LOG  (email/SMS bodies sent to candidates)
-------------------------------------------------------------------------------
UPDATE dbo.Message       SET Body = N'[anonymisert]';
UPDATE dbo.SmsMessage    SET Message = N'[anonymisert]', LastError = NULL;
UPDATE dbo.Notification  SET Message = N'[anonymisert]';
UPDATE dbo.DialogMessage SET Message = N'[anonymisert]';
UPDATE dbo.EmailMessage  SET EmailType = EmailType;   -- no PII in this col; placeholder no-op for documentation

-------------------------------------------------------------------------------
-- 10. AUDIT / DIAGNOSTIC LOGS  (IP, usernames, serilog properties)
-------------------------------------------------------------------------------
UPDATE dbo.AdminAuditLog SET AdminUserName = N'admin', IpAddress = NULL,
                             TargetCompanyName = N'[company]', Details = NULL;
UPDATE dbo.FileAccessLog SET Username = N'user', IpAddress = NULL, OriginalFileName = N'document.pdf';
UPDATE dbo.Logs          SET Message = N'[anonymisert]', Exception = NULL,
                             Properties = NULL, MessageTemplate = NULL;

-------------------------------------------------------------------------------
-- 11. LEGACY VERIFIND IMPORT TABLES  (full candidate/customer PII snapshots)
--     These mirror imported VeriFind data — scrub the lot.
-------------------------------------------------------------------------------
;WITH lo AS (SELECT Id, ROW_NUMBER() OVER (ORDER BY Id) AS rn FROM dbo.LegacyVeriFindOrder)
UPDATE l SET
    l.CandidateFirstName  = N'Testkandidat',
    l.CandidateLastName   = N'Nr' + CAST(lo.rn AS nvarchar(20)),
    l.CandidateMaidenName = NULL,
    l.CandidateEmail      = 'candidate' + CAST(lo.rn AS varchar(20)) + '@example.invalid',
    l.CandidatePhoneNumber= '4740000000',
    l.CandidateBirthDate  = NULL,
    l.OrderCustomerCompanyName = N'Testbedrift',
    l.OrderCustomerEmail  = 'customer@example.invalid',
    l.OrderCustomerPhoneNumber = '4740000000',
    l.SourceJson          = NULL
FROM dbo.LegacyVeriFindOrder l JOIN lo ON lo.Id = l.Id;

UPDATE dbo.LegacyVeriFindUser     SET Name = N'Test User', UserName = N'user@example.invalid',
                                      Email = 'user@example.invalid', PhoneNumber = '4740000000',
                                      SourceJson = NULL, RolesJson = NULL;
UPDATE dbo.LegacyVeriFindCustomer SET Name = N'Testkunde', SourceJson = NULL;
UPDATE dbo.LegacyVeriFindCandidateLogin SET Email = 'candidate@example.invalid', SourceJson = NULL;
UPDATE dbo.LegacyVeriFindOrderLine SET SourceJson = NULL;
UPDATE dbo.LegacyVeriFindOrderTag SET SourceJson = NULL;
UPDATE dbo.LegacyVeriFindTag      SET SourceJson = NULL;
UPDATE dbo.LegacyVeriFindImportJob SET SourceCustomerName = N'Testkunde', SummaryJson = NULL, ErrorMessage = NULL;

COMMIT;
PRINT 'CheckIris PII anonymization complete.';
