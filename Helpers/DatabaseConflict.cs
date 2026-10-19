using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EduLibrary.Helpers;

public static class DatabaseConflict
{
    public static bool IsExpected(Exception exception)
    {
        // EF may wrap SQL deadlocks in an execution-strategy exception.
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateConcurrencyException) return true;
            if (current is not SqlException sqlException) continue;
            foreach (SqlError error in sqlException.Errors)
                if (error.Number is 1205 or 2601 or 2627) return true;
        }
        return false;
    }
}
