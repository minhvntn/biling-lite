import {
  BadRequestException,
  Body,
  Controller,
  Get,
  Param,
  Patch,
  Post,
  Res,
  UnauthorizedException,
  UploadedFile,
  UseInterceptors,
} from '@nestjs/common';
import * as bcrypt from 'bcrypt';
import { FileInterceptor } from '@nestjs/platform-express';
import { Response } from 'express';
import { PrismaService } from '../prisma/prisma.service';
import { UpdateBackupSettingsDto } from './dto/update-backup-settings.dto';
import { SettingsBackupService } from './settings-backup.service';

const AGENT_ADMIN_USERNAME_KEY = 'AGENT_ADMIN_USERNAME';
const AGENT_ADMIN_PASSWORD_HASH_KEY = 'AGENT_ADMIN_PASSWORD_HASH';
const DEFAULT_ADMIN_USERNAME = 'admin';
const DEFAULT_ADMIN_PASSWORD = 'admin';

@Controller('settings')
export class SettingsController {
  constructor(
    private readonly prisma: PrismaService,
    private readonly backupService: SettingsBackupService,
  ) {}

  @Get()
  async getSettings() {
    const settings = await this.prisma.appSetting.findMany();
    const result = settings.reduce((acc, curr) => {
      // Never expose password hash to clients
      if (curr.key !== AGENT_ADMIN_PASSWORD_HASH_KEY) {
        acc[curr.key] = curr.value;
      }
      return acc;
    }, {} as Record<string, string>);

    // Always expose the current admin username (masked value not hash)
    if (!result[AGENT_ADMIN_USERNAME_KEY]) {
      result[AGENT_ADMIN_USERNAME_KEY] = DEFAULT_ADMIN_USERNAME;
    }
    return result;
  }

  @Post()
  async updateSetting(@Body() body: { key: string; value: string }) {
    return this.prisma.appSetting.upsert({
      where: { key: body.key },
      update: { value: body.value },
      create: { key: body.key, value: body.value },
    });
  }

  /**
   * Agent clients call this to verify agent-admin credentials.
   * Returns ok: true if valid, throws 401 otherwise.
   */
  @Post('agent-admin/login')
  async agentAdminLogin(
    @Body() body: { username: string; password: string },
  ) {
    const { username, password } = body;
    if (!username || !password) {
      throw new BadRequestException('username and password are required');
    }

    const [usernameRecord, passwordHashRecord] = await Promise.all([
      this.prisma.appSetting.findUnique({
        where: { key: AGENT_ADMIN_USERNAME_KEY },
      }),
      this.prisma.appSetting.findUnique({
        where: { key: AGENT_ADMIN_PASSWORD_HASH_KEY },
      }),
    ]);

    const storedUsername = usernameRecord?.value ?? DEFAULT_ADMIN_USERNAME;

    if (username.toLowerCase() !== storedUsername.toLowerCase()) {
      throw new UnauthorizedException('Sai tài khoản hoặc mật khẩu quản trị');
    }

    // If no hash stored yet, fall back to default password
    if (!passwordHashRecord?.value) {
      if (password !== DEFAULT_ADMIN_PASSWORD) {
        throw new UnauthorizedException('Sai tài khoản hoặc mật khẩu quản trị');
      }
      return { ok: true };
    }

    const valid = await bcrypt.compare(password, passwordHashRecord.value);
    if (!valid) {
      throw new UnauthorizedException('Sai tài khoản hoặc mật khẩu quản trị');
    }

    return { ok: true };
  }

  /**
   * Admin dashboard calls this to change the agent-admin credentials.
   */
  @Post('agent-admin/change-password')
  async changeAgentAdminPassword(
    @Body()
    body: {
      currentPassword: string;
      newUsername: string;
      newPassword: string;
    },
  ) {
    const { currentPassword, newUsername, newPassword } = body;
    if (!currentPassword || !newUsername || !newPassword) {
      throw new BadRequestException(
        'currentPassword, newUsername and newPassword are required',
      );
    }

    if (newPassword.length < 4) {
      throw new BadRequestException('Mật khẩu mới phải có ít nhất 4 ký tự');
    }

    // Verify current password first
    const [usernameRecord, passwordHashRecord] = await Promise.all([
      this.prisma.appSetting.findUnique({
        where: { key: AGENT_ADMIN_USERNAME_KEY },
      }),
      this.prisma.appSetting.findUnique({
        where: { key: AGENT_ADMIN_PASSWORD_HASH_KEY },
      }),
    ]);

    if (!passwordHashRecord?.value) {
      // No password set yet, default is 'admin'
      if (currentPassword !== DEFAULT_ADMIN_PASSWORD) {
        throw new UnauthorizedException('Mật khẩu hiện tại không đúng');
      }
    } else {
      const valid = await bcrypt.compare(
        currentPassword,
        passwordHashRecord.value,
      );
      if (!valid) {
        throw new UnauthorizedException('Mật khẩu hiện tại không đúng');
      }
    }

    const newHash = await bcrypt.hash(newPassword, 10);

    await Promise.all([
      this.prisma.appSetting.upsert({
        where: { key: AGENT_ADMIN_USERNAME_KEY },
        update: { value: newUsername },
        create: { key: AGENT_ADMIN_USERNAME_KEY, value: newUsername },
      }),
      this.prisma.appSetting.upsert({
        where: { key: AGENT_ADMIN_PASSWORD_HASH_KEY },
        update: { value: newHash },
        create: { key: AGENT_ADMIN_PASSWORD_HASH_KEY, value: newHash },
      }),
    ]);

    return { ok: true, username: newUsername };
  }

  /**
   * Admin desktop quick flow: update agent-admin credentials directly
   * by providing only username + password.
   */
  @Post('agent-admin/update-credentials')
  async updateAgentAdminCredentials(
    @Body()
    body: {
      username: string;
      password: string;
    },
  ) {
    const username = body.username?.trim();
    const password = body.password;

    if (!username || !password) {
      throw new BadRequestException('username and password are required');
    }

    if (password.length < 4) {
      throw new BadRequestException('Mat khau moi phai co it nhat 4 ky tu');
    }

    const newHash = await bcrypt.hash(password, 10);

    await Promise.all([
      this.prisma.appSetting.upsert({
        where: { key: AGENT_ADMIN_USERNAME_KEY },
        update: { value: username },
        create: { key: AGENT_ADMIN_USERNAME_KEY, value: username },
      }),
      this.prisma.appSetting.upsert({
        where: { key: AGENT_ADMIN_PASSWORD_HASH_KEY },
        update: { value: newHash },
        create: { key: AGENT_ADMIN_PASSWORD_HASH_KEY, value: newHash },
      }),
    ]);

    return { ok: true, username };
  }

  @Get('backup')
  async getBackupSettings() {
    return this.backupService.getBackupSettings();
  }

  @Patch('backup')
  async updateBackupSettings(@Body() payload: UpdateBackupSettingsDto) {
    return this.backupService.updateBackupSettings(payload);
  }

  @Post('backup/run')
  async runBackupNow(@Body() body: { requestedBy?: string }) {
    return this.backupService.runBackupNow(body?.requestedBy ?? 'admin.desktop');
  }

  @Get('backup/files')
  async listBackupFiles() {
    return this.backupService.listBackupFiles();
  }

  @Get('backup/files/:fileName')
  async downloadBackupFile(
    @Param('fileName') fileName: string,
    @Res() response: Response,
  ) {
    const file = await this.backupService.getBackupFileForDownload(fileName);
    response.setHeader('Content-Type', 'application/json; charset=utf-8');
    response.setHeader(
      'Content-Disposition',
      `attachment; filename="${file.fileName}"`,
    );
    return response.sendFile(file.fileName, {
      root: file.directory,
    });
  }

  @Post('backup/import')
  @UseInterceptors(FileInterceptor('file'))
  async importBackup(
    @UploadedFile() file: any,
    @Body('requestedBy') requestedBy: string,
  ) {
    if (!file || !file.buffer) {
      throw new BadRequestException('Missing backup file');
    }

    return this.backupService.importBackupFromBuffer(
      file.originalname ?? 'backup.json',
      file.buffer,
      requestedBy ?? 'admin.desktop',
    );
  }
}
