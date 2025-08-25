namespace Downloadr.Cli;

using Infrastructure.Bootstrapping;

public static class Program
{
    public static int Main( string[] args )
    {
        var app = Bootstrapper.Bootstrap();
        if ( args == null || args.Length == 0 )
        {
            return app.Run( new[] { "top" } );
        }

        // If invoked with only options (e.g., --parallel), implicitly target the top command
        if ( args.Length > 0 && args[0].StartsWith( "-" ) )
        {
            var forwarded = new string[ args.Length + 1 ];
            forwarded[0] = "top";
            Array.Copy( args, 0, forwarded, 1, args.Length );
            return app.Run( forwarded );
        }

        return app.Run( args );
    }
}