using Helpdesk.Domain.Tickets;

namespace Helpdesk.UnitTests.Domain;

public sealed class PriorityMatrixTests
{
    [Theory]
    [InlineData(Impact.High, Urgency.High, Priority.Critical)]
    [InlineData(Impact.High, Urgency.Medium, Priority.High)]
    [InlineData(Impact.High, Urgency.Low, Priority.Medium)]
    [InlineData(Impact.Medium, Urgency.High, Priority.High)]
    [InlineData(Impact.Medium, Urgency.Medium, Priority.Medium)]
    [InlineData(Impact.Medium, Urgency.Low, Priority.Low)]
    [InlineData(Impact.Low, Urgency.High, Priority.Medium)]
    [InlineData(Impact.Low, Urgency.Medium, Priority.Low)]
    [InlineData(Impact.Low, Urgency.Low, Priority.Planning)]
    public void Calculate_follows_the_itil_impact_by_urgency_matrix(Impact impact, Urgency urgency, Priority expected) =>
        PriorityMatrix.Calculate(impact, urgency).Should().Be(expected);

    [Fact]
    public void Calculate_rejects_values_outside_the_matrix()
    {
        var act = () => PriorityMatrix.Calculate((Impact)7, Urgency.Low);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Calculate_rejects_undefined_urgency()
    {
        var act = () => PriorityMatrix.Calculate(Impact.Low, (Urgency)0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
