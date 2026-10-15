namespace BudgetApp.Services;

/// <summary>A problem the user should see, worded for them.</summary>
public class AdvisorException(string message) : Exception(message);
