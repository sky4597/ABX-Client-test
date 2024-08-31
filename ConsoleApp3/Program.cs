using ConsoleApp3;

class Program
{
    static void Main(string[] args)
    {
        ABXClient client = new ABXClient();

        try
        {
            client.StreamAllPackets();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
        }
        finally
        {
            client.Close();
        }
    }
}