using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Server.Core.Domain;

[Table("People")]
public class Person
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [MaxLength(10)]
    [Column(TypeName = "char(10)")]
    public required string IamId { get; set; }

    [MaxLength(8)]
    [Column(TypeName = "char(8)")]
    public string? EmployeeId { get; set; }

    [MaxLength(9)]
    [Column(TypeName = "char(9)")]
    public string? StudentId { get; set; }

    [MaxLength(10)]
    [Column(TypeName = "char(10)")]
    public string? ExternalId { get; set; }

    [MaxLength(64)]
    [Column(TypeName = "nvarchar(64)")]
    public string? FirstName { get; set; }

    [MaxLength(64)]
    [Column(TypeName = "nvarchar(64)")]
    public string? MiddleName { get; set; }

    [MaxLength(64)]
    [Column(TypeName = "nvarchar(64)")]
    public string? LastName { get; set; }

    [MaxLength(16)]
    [Column(TypeName = "nvarchar(16)")]
    public string? Suffix { get; set; }

    [MaxLength(128)]
    [Column(TypeName = "nvarchar(128)")]
    public string? FullName { get; set; }

    [MaxLength(64)]
    [Column(TypeName = "nvarchar(64)")]
    public string? Pronouns { get; set; }

    public bool IsActiveInIam { get; set; }

    public bool? IsEmployee { get; set; }

    public bool? IsHsEmployee { get; set; }

    public bool? IsFaculty { get; set; }

    public bool? IsStudent { get; set; }

    public bool? IsStaff { get; set; }

    public bool? IsExternal { get; set; }

    [MaxLength(1)]
    [Column(TypeName = "char(1)")]
    public string? PrivacyCode { get; set; }

    [MaxLength(1)]
    [Column(TypeName = "char(1)")]
    public string? IsCampusEmployee { get; set; }

    [MaxLength(8)]
    [Column(TypeName = "char(8)")]
    public string? UserId { get; set; }

    [MaxLength(128)]
    [Column(TypeName = "varchar(128)")]
    public string? Email { get; set; }

    [Column(TypeName = "datetime2(6)")]
    public DateTime? ModifyDate { get; set; }

    [MaxLength(19)]
    [Column(TypeName = "char(19)")]
    public string? ModifyDateRaw { get; set; }

    [Column(TypeName = "datetime2(6)")]
    public DateTime? FirstIngestedAt { get; set; }

    [Column(TypeName = "datetime2(6)")]
    public DateTime? LastFetchedAt { get; set; }

    [MaxLength(36)]
    [Column(TypeName = "char(36)")]
    public string? LastRunId { get; set; }

    [MaxLength(128)]
    [Column(TypeName = "varchar(128)")]
    public string? SourceEndpoint { get; set; }

    [Column(TypeName = "datetime2(6)")]
    public DateTime? PromotedAt { get; set; }

    [MaxLength(36)]
    [Column(TypeName = "char(36)")]
    public string? PromotionRunId { get; set; }
}
