import { Controller, Post, Get, Delete, Body, Param } from '@nestjs/common';
import { AuthAdminService } from './auth-admin.service';
import { AdminLoginDto, CreateAdminUserDto } from './dto/auth-admin.dto';

@Controller('auth/admin')
export class AuthAdminController {
  constructor(private readonly authAdminService: AuthAdminService) {}

  @Post('login')
  login(@Body() dto: AdminLoginDto) {
    return this.authAdminService.login(dto);
  }

  @Post('users')
  createStaff(@Body() dto: CreateAdminUserDto) {
    return this.authAdminService.createStaff(dto);
  }

  @Get('users')
  getUsers() {
    return this.authAdminService.getUsers();
  }

  @Delete('users/:id')
  deleteUser(@Param('id') id: string) {
    return this.authAdminService.deleteUser(id);
  }

  @Post('update-admin')
  updateAdmin(@Body() dto: CreateAdminUserDto) {
    return this.authAdminService.updateAdminAccount(dto);
  }
}
