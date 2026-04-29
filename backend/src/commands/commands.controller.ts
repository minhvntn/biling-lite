import { Body, Controller, Get, Param, Post } from '@nestjs/common';
import { CommandsService } from './commands.service';
import { RequestCommandDto } from './dto/request-command.dto';
import { NotifyPcDto } from './dto/notify-pc.dto';

@Controller()
export class CommandsController {
  constructor(private readonly commandsService: CommandsService) {}

  @Post('pcs/:pcId/open')
  async openPc(@Param('pcId') pcId: string, @Body() body: RequestCommandDto) {
    return this.commandsService.createOpenCommand(pcId, body.requestedBy);
  }

  @Post('pcs/:pcId/lock')
  async lockPc(@Param('pcId') pcId: string, @Body() body: RequestCommandDto) {
    return this.commandsService.createLockCommand(pcId, body.requestedBy);
  }

  @Post('pcs/:pcId/restart')
  async restartPc(@Param('pcId') pcId: string, @Body() body: RequestCommandDto) {
    return this.commandsService.createRestartCommand(pcId, body.requestedBy);
  }

  @Post('pcs/:pcId/shutdown')
  async shutdownPc(@Param('pcId') pcId: string, @Body() body: RequestCommandDto) {
    return this.commandsService.createShutdownCommand(pcId, body.requestedBy);
  }

  @Post('pcs/:pcId/close-apps')
  async closeApps(@Param('pcId') pcId: string, @Body() body: RequestCommandDto) {
    return this.commandsService.createCloseAppsCommand(pcId, body.requestedBy);
  }

  @Post('pcs/:pcId/pause')
  async pausePc(@Param('pcId') pcId: string, @Body() body: RequestCommandDto) {
    return this.commandsService.createPauseCommand(pcId, body.requestedBy);
  }

  @Post('pcs/:pcId/resume')
  async resumePc(@Param('pcId') pcId: string, @Body() body: RequestCommandDto) {
    return this.commandsService.createResumeCommand(pcId, body.requestedBy);
  }

  @Post('pcs/:pcId/notify')
  async notifyPc(@Param('pcId') pcId: string, @Body() body: NotifyPcDto) {
    return this.commandsService.notifyPc(pcId, body.message, body.requestedBy);
  }

  @Get('commands/:commandId')
  async getCommand(@Param('commandId') commandId: string) {
    return this.commandsService.getCommandById(commandId);
  }
}
