using System.Data.Common;
using BuildNexus.DesignService.Models;
using MySqlConnector;

namespace BuildNexus.DesignService.Data;

/// <summary>
/// ADO.NET data access for <c>design_documents</c> and
/// <c>design_document_versions</c>. Direct SQL only — no ORM. Every value
/// reaches MySQL as a bound parameter.
/// </summary>
public class DesignDocumentRepository : IDesignDocumentRepository
{
    private const string DocumentColumns =
        "id, project_id, name, created_by, created_at";

    /// <summary>
    /// Deliberately without <c>file_bytes</c>: a listing reads many versions and
    /// never the content, and bytes that are never read cannot be sent by
    /// mistake.
    /// </summary>
    private const string VersionMetadataColumns =
        "id, document_id, version_number, file_name, content_type, file_size_bytes, "
        + "status, revision_comment, uploaded_by, uploaded_at";

    /// <summary>
    /// One upload racing another for the same next version number loses to
    /// <c>uq_design_document_versions_number</c>; a few retries is far more than
    /// two Architects saving at the same instant will ever need.
    /// </summary>
    private const int MaxVersionInsertAttempts = 5;

    private readonly IDbConnectionFactory _connectionFactory;

    public DesignDocumentRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<DesignUploadResult> AddVersionAsync(DesignUpload upload)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await TryAddVersionAsync(upload);
            }
            catch (MySqlException ex)
                when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry && attempt < MaxVersionInsertAttempts)
            {
                // Another upload created the document, or claimed this version
                // number, between our read and our write. Start over: the retry
                // sees the row they wrote and takes the number after it.
            }
        }
    }

    private async Task<DesignUploadResult> TryAddVersionAsync(DesignUpload upload)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        // The document lookup-or-create and the version insert are one unit: a
        // document created without a first version is a document that never
        // existed as far as anyone should see, and a version number read in one
        // transaction must still be free when this one writes it.
        await using var transaction = await connection.BeginTransactionAsync();

        var document = await FindDocumentAsync(connection, transaction, upload.ProjectId, upload.DocumentName)
            ?? await InsertDocumentAsync(connection, transaction, upload);

        var versionNumber = await NextVersionNumberAsync(connection, transaction, document.Id);

        var version = new DesignDocumentVersion
        {
            Id = Guid.NewGuid(),
            DocumentId = document.Id,
            VersionNumber = versionNumber,
            FileName = upload.FileName,
            ContentType = upload.ContentType,
            FileSizeBytes = upload.Content.LongLength,
            // US-09: every upload lands as Submitted, and the caller has no say
            // in it.
            Status = DesignDocumentStatus.Submitted,
            RevisionComment = upload.RevisionComment,
            UploadedBy = upload.UploadedBy,
            UploadedAt = upload.UploadedAtUtc
        };

        await InsertVersionAsync(connection, transaction, version, upload.Content);

        await transaction.CommitAsync();

        return new DesignUploadResult { Document = document, Version = version };
    }

    public async Task<IReadOnlyList<DesignDocumentWithVersions>> ListForProjectAsync(Guid projectId)
    {
        const string documentsSql = $@"
            SELECT {DocumentColumns}
            FROM design_documents
            WHERE project_id = @projectId
            ORDER BY created_at, name;";

        // Joined back to design_documents purely to filter by project — the
        // versions carry no project_id of their own, by design.
        const string versionsSql = $@"
            SELECT v.id, v.document_id, v.version_number, v.file_name, v.content_type,
                   v.file_size_bytes, v.status, v.revision_comment, v.uploaded_by, v.uploaded_at
            FROM design_document_versions v
            INNER JOIN design_documents d ON d.id = v.document_id
            WHERE d.project_id = @projectId
            ORDER BY v.document_id, v.version_number;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();

        var documents = new List<DesignDocument>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = documentsSql;
            AddParameter(command, "@projectId", projectId);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                documents.Add(MapDocument(reader));
            }
        }

        var versionsByDocument = new Dictionary<Guid, List<DesignDocumentVersion>>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = versionsSql;
            AddParameter(command, "@projectId", projectId);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var version = MapVersionMetadata(reader);

                if (!versionsByDocument.TryGetValue(version.DocumentId, out var list))
                {
                    list = [];
                    versionsByDocument[version.DocumentId] = list;
                }

                list.Add(version);
            }
        }

        return [.. documents.Select(document => new DesignDocumentWithVersions
        {
            Document = document,
            Versions = versionsByDocument.TryGetValue(document.Id, out var versions)
                ? versions
                : []
        })];
    }

    public async Task<StoredDesignFile?> GetVersionFileAsync(Guid versionId)
    {
        const string sql = @"
            SELECT v.id, v.file_name, v.content_type, v.file_bytes, d.project_id
            FROM design_document_versions v
            INNER JOIN design_documents d ON d.id = v.document_id
            WHERE v.id = @versionId
            LIMIT 1;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@versionId", versionId);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new StoredDesignFile
        {
            VersionId = reader.GetGuid(reader.GetOrdinal("id")),
            ProjectId = reader.GetGuid(reader.GetOrdinal("project_id")),
            FileName = reader.GetString(reader.GetOrdinal("file_name")),
            ContentType = reader.GetString(reader.GetOrdinal("content_type")),
            Content = (byte[])reader["file_bytes"]
        };
    }

    private static async Task<DesignDocument?> FindDocumentAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid projectId,
        string name)
    {
        // FOR UPDATE so a concurrent upload under the same name waits here rather
        // than both sides deciding the document does not exist and both
        // inserting it.
        const string sql = $@"
            SELECT {DocumentColumns}
            FROM design_documents
            WHERE project_id = @projectId AND name = @name
            LIMIT 1
            FOR UPDATE;";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameter(command, "@projectId", projectId);
        AddParameter(command, "@name", name);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapDocument(reader) : null;
    }

    private static async Task<DesignDocument> InsertDocumentAsync(
        DbConnection connection,
        DbTransaction transaction,
        DesignUpload upload)
    {
        const string sql = @"
            INSERT INTO design_documents (id, project_id, name, created_by, created_at)
            VALUES (@id, @projectId, @name, @createdBy, @createdAt);";

        var document = new DesignDocument
        {
            Id = Guid.NewGuid(),
            ProjectId = upload.ProjectId,
            Name = upload.DocumentName,
            CreatedBy = upload.UploadedBy,
            CreatedAt = upload.UploadedAtUtc
        };

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameter(command, "@id", document.Id);
        AddParameter(command, "@projectId", document.ProjectId);
        AddParameter(command, "@name", document.Name);
        AddParameter(command, "@createdBy", document.CreatedBy);
        AddParameter(command, "@createdAt", document.CreatedAt);

        // A DuplicateKeyEntry here means a concurrent first upload won the race;
        // AddVersionAsync catches it and retries, and the retry finds their row.
        await command.ExecuteNonQueryAsync();

        return document;
    }

    private static async Task<int> NextVersionNumberAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid documentId)
    {
        const string sql = @"
            SELECT COALESCE(MAX(version_number), 0) + 1
            FROM design_document_versions
            WHERE document_id = @documentId;";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameter(command, "@documentId", documentId);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task InsertVersionAsync(
        DbConnection connection,
        DbTransaction transaction,
        DesignDocumentVersion version,
        byte[] content)
    {
        const string sql = @"
            INSERT INTO design_document_versions
                (id, document_id, version_number, file_name, content_type, file_size_bytes,
                 file_bytes, status, revision_comment, uploaded_by, uploaded_at)
            VALUES
                (@id, @documentId, @versionNumber, @fileName, @contentType, @fileSizeBytes,
                 @fileBytes, @status, @revisionComment, @uploadedBy, @uploadedAt);";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameter(command, "@id", version.Id);
        AddParameter(command, "@documentId", version.DocumentId);
        AddParameter(command, "@versionNumber", version.VersionNumber);
        AddParameter(command, "@fileName", version.FileName);
        AddParameter(command, "@contentType", version.ContentType);
        AddParameter(command, "@fileSizeBytes", version.FileSizeBytes);
        AddParameter(command, "@fileBytes", content);
        // Stored as the enum's name, which is what ck_design_document_versions_status
        // checks against — never its underlying number.
        AddParameter(command, "@status", version.Status.ToString());
        AddParameter(command, "@revisionComment", version.RevisionComment);
        AddParameter(command, "@uploadedBy", version.UploadedBy);
        AddParameter(command, "@uploadedAt", version.UploadedAt);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<DesignVersionForReview?> GetVersionForReviewAsync(Guid versionId)
    {
        const string sql = @"
            SELECT v.id, v.document_id, v.version_number, v.status, v.uploaded_by, d.project_id, d.name
            FROM design_document_versions v
            INNER JOIN design_documents d ON d.id = v.document_id
            WHERE v.id = @versionId
            LIMIT 1;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@versionId", versionId);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new DesignVersionForReview
        {
            VersionId = reader.GetGuid(reader.GetOrdinal("id")),
            DocumentId = reader.GetGuid(reader.GetOrdinal("document_id")),
            VersionNumber = reader.GetInt32(reader.GetOrdinal("version_number")),
            Status = Enum.Parse<DesignDocumentStatus>(reader.GetString(reader.GetOrdinal("status"))),
            UploadedBy = reader.GetGuid(reader.GetOrdinal("uploaded_by")),
            ProjectId = reader.GetGuid(reader.GetOrdinal("project_id")),
            DocumentName = reader.GetString(reader.GetOrdinal("name"))
        };
    }

    public async Task<ReviewDecisionOutcome> RecordReviewDecisionAsync(
        ReviewDecision decision, IReadOnlyList<OutboxEvent> outboxEvents)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        // One transaction, the document row locked for its length: two review
        // decisions for the same document — even on two different versions —
        // must resolve one after the other, never both, or the "one Approved
        // version locks the rest" rule could be beaten by a race.
        await using var transaction = await connection.BeginTransactionAsync();

        await LockDocumentAsync(connection, transaction, decision.DocumentId);

        var currentStatus = await LockAndReadVersionStatusAsync(connection, transaction, decision.VersionId);

        if (currentStatus is null)
        {
            await transaction.RollbackAsync();
            return ReviewDecisionOutcome.VersionNotFound;
        }

        if (currentStatus != DesignDocumentStatus.Submitted)
        {
            await transaction.RollbackAsync();
            return ReviewDecisionOutcome.AlreadyDecided;
        }

        if (await AnotherVersionIsApprovedAsync(connection, transaction, decision.DocumentId, decision.VersionId))
        {
            await transaction.RollbackAsync();
            return ReviewDecisionOutcome.DocumentAlreadyApproved;
        }

        await ApplyReviewDecisionAsync(connection, transaction, decision);

        // Written in the same transaction as the decision, so a DesignApproved
        // event commits with the approval it describes or not at all — the
        // reason the outbox pattern exists at all.
        foreach (var outboxEvent in outboxEvents)
        {
            await OutboxRepository.InsertAsync(connection, transaction, outboxEvent);
        }

        await transaction.CommitAsync();

        return ReviewDecisionOutcome.Recorded;
    }

    private static async Task LockDocumentAsync(DbConnection connection, DbTransaction transaction, Guid documentId)
    {
        const string sql = "SELECT id FROM design_documents WHERE id = @documentId FOR UPDATE;";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameter(command, "@documentId", documentId);

        await command.ExecuteScalarAsync();
    }

    private static async Task<DesignDocumentStatus?> LockAndReadVersionStatusAsync(
        DbConnection connection, DbTransaction transaction, Guid versionId)
    {
        const string sql = "SELECT status FROM design_document_versions WHERE id = @versionId FOR UPDATE;";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameter(command, "@versionId", versionId);

        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync() ? Enum.Parse<DesignDocumentStatus>(reader.GetString(0)) : null;
    }

    /// <summary>
    /// True if some version of this document other than <paramref name="versionId"/>
    /// is already Approved — the target version's own status is already known
    /// to be Submitted by the time this runs, so excluding it only guards
    /// against re-checking what the caller just confirmed.
    /// </summary>
    private static async Task<bool> AnotherVersionIsApprovedAsync(
        DbConnection connection, DbTransaction transaction, Guid documentId, Guid versionId)
    {
        const string sql = @"
            SELECT COUNT(*) FROM design_document_versions
            WHERE document_id = @documentId AND status = 'Approved' AND id <> @versionId;";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameter(command, "@documentId", documentId);
        AddParameter(command, "@versionId", versionId);

        return Convert.ToInt64(await command.ExecuteScalarAsync()) > 0;
    }

    private static async Task ApplyReviewDecisionAsync(
        DbConnection connection, DbTransaction transaction, ReviewDecision decision)
    {
        const string sql = @"
            UPDATE design_document_versions
            SET status = @status, reviewed_by = @reviewedBy, reviewed_at = @reviewedAt, review_comment = @reviewComment
            WHERE id = @versionId;";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        // Stored as the enum's name, which is what ck_design_document_versions_status
        // checks against — never its underlying number.
        AddParameter(command, "@status", decision.Status.ToString());
        AddParameter(command, "@reviewedBy", decision.ReviewedBy);
        AddParameter(command, "@reviewedAt", decision.ReviewedAtUtc);
        AddParameter(command, "@reviewComment", decision.ReviewComment);
        AddParameter(command, "@versionId", decision.VersionId);

        await command.ExecuteNonQueryAsync();
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        // An omitted revision comment is a real NULL in the column, not an empty
        // string.
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static string? GetNullableString(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DesignDocument MapDocument(DbDataReader reader) => new()
    {
        // MySqlConnector surfaces CHAR(36) as a Guid, not a string.
        Id = reader.GetGuid(reader.GetOrdinal("id")),
        ProjectId = reader.GetGuid(reader.GetOrdinal("project_id")),
        Name = reader.GetString(reader.GetOrdinal("name")),
        CreatedBy = reader.GetGuid(reader.GetOrdinal("created_by")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at"))
    };

    private static DesignDocumentVersion MapVersionMetadata(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("id")),
        DocumentId = reader.GetGuid(reader.GetOrdinal("document_id")),
        VersionNumber = reader.GetInt32(reader.GetOrdinal("version_number")),
        FileName = reader.GetString(reader.GetOrdinal("file_name")),
        ContentType = reader.GetString(reader.GetOrdinal("content_type")),
        FileSizeBytes = reader.GetInt64(reader.GetOrdinal("file_size_bytes")),
        Status = Enum.Parse<DesignDocumentStatus>(reader.GetString(reader.GetOrdinal("status"))),
        RevisionComment = GetNullableString(reader, "revision_comment"),
        UploadedBy = reader.GetGuid(reader.GetOrdinal("uploaded_by")),
        UploadedAt = reader.GetDateTime(reader.GetOrdinal("uploaded_at"))
    };
}
