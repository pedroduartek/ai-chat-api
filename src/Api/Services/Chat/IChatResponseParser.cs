namespace Api.Services.Chat;

public interface IChatResponseParser
{
    string Parse(string rawResponse);
}
