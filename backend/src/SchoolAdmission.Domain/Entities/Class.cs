namespace SchoolAdmission.Domain.Entities;

/// <summary>
/// Domain Entity representing an academic class with an allocated seat capacity.
/// Pure Domain Model: Fully encapsulated with zero external framework dependencies.
/// </summary>
public sealed class Class
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int TotalSeats { get; private set; }
    public decimal AdmissionFee { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    // Navigation property for EF Core
    public ICollection<Seat> Seats { get; private set; } = new List<Seat>();

    // Private constructor for ORM reflection
    private Class() { }

    /// <summary>
    /// Factory method to enforce rich domain rules at instantiation.
    /// </summary>
    public static Class Create(string name, int totalSeats, decimal admissionFee)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Class name cannot be empty.", nameof(name));

        if (totalSeats <= 0)
            throw new ArgumentOutOfRangeException(nameof(totalSeats), "Total seats must be greater than zero.");

        if (admissionFee < 0)
            throw new ArgumentOutOfRangeException(nameof(admissionFee), "Admission fee cannot be negative.");

        var instance = new Class
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            TotalSeats = totalSeats,
            AdmissionFee = admissionFee,
            CreatedAtUtc = DateTime.UtcNow
        };

        // Initialize corresponding seats
        for (int i = 1; i <= totalSeats; i++)
        {
            instance.Seats.Add(Seat.Create(instance.Id, i));
        }

        return instance;
    }
}
