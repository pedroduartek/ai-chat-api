namespace Api.Services;

public interface IChatResponseParser
{
    string Parse(string rawResponse);
}
