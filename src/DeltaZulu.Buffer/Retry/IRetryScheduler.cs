namespace DeltaZulu.Buffer.Retry;

public interface IRetryScheduler
{
    DateTimeOffset CalculateNextAttempt(int attemptCount);

    bool IsRetryExhausted(int attemptCount);
}