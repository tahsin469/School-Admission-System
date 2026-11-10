namespace SchoolAdmission.Application.Exceptions;

public abstract class AdmissionException : Exception
{
    protected AdmissionException(string message) : base(message) { }
}

public class NotFoundException : AdmissionException
{
    public NotFoundException(string message) : base(message) { }
}

public class SeatConflictException : AdmissionException
{
    public SeatConflictException(string message) : base(message) { }
}

public class BusinessRuleException : AdmissionException
{
    public BusinessRuleException(string message) : base(message) { }
}
