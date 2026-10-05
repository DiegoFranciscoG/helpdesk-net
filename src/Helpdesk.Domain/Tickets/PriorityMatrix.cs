namespace Helpdesk.Domain.Tickets;

/// <summary>
/// Matriz ITIL impacto × urgencia (fuentes #24 y #25). Con 1 = alto en ambos ejes, la tabla
/// equivale a <c>prioridad = impacto + urgencia − 1</c>; la base de datos valida la misma igualdad.
/// </summary>
public static class PriorityMatrix
{
    public static Priority Calculate(Impact impact, Urgency urgency)
    {
        if (!Enum.IsDefined(impact))
        {
            throw new ArgumentOutOfRangeException(nameof(impact), impact, "Impacto fuera de rango.");
        }

        if (!Enum.IsDefined(urgency))
        {
            throw new ArgumentOutOfRangeException(nameof(urgency), urgency, "Urgencia fuera de rango.");
        }

        return (Priority)((int)impact + (int)urgency - 1);
    }
}
