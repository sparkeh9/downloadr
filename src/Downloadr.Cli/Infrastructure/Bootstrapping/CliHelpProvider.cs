namespace Downloadr.Cli.Infrastructure.Bootstrapping;

using System.Collections.Generic;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Help;
using Spectre.Console.Rendering;

public class CliHelpProvider( ICommandAppSettings settings ) : HelpProvider( settings )
{
    public override IEnumerable<IRenderable> GetHeader( ICommandModel model, ICommandInfo? command )
    {
        return base.GetHeader( model, command );
        
        if ( command == null )
        {
            return
            [
                new Text( "Downloadr CLI - Command Line Interface for TwitchHID",
                          new Style( Color.Cyan1, decoration: Decoration.Bold ) )
            ];
        }

        return base.GetHeader( model, command );
    }

    public override IEnumerable<IRenderable> GetFooter( ICommandModel model, ICommandInfo? command )
    {
        return base.GetFooter( model, command );
        if ( command != null )

            return base.GetFooter( model, command );

        // Return empty footer to avoid overlap with examples
        return [];
    }

    public override IEnumerable<IRenderable> GetExamples( ICommandModel model, ICommandInfo? command )
    {
        return base.GetExamples( model, command );
        if ( command != null )
            return base.GetExamples( model, command );

        var examplesPanel = new Panel(
            new Markup( """

                        """ ) )
        {
            Header = new PanelHeader( " Examples by Category " ),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style( Color.Green )
        };

        return [examplesPanel];
    }
};