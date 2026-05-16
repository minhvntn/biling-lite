import { Injectable, UnauthorizedException, BadRequestException, OnModuleInit } from '@nestjs/common';
import { PrismaService } from '../prisma/prisma.service';
import * as bcrypt from 'bcrypt';
import { AdminLoginDto, CreateAdminUserDto } from './dto/auth-admin.dto';

@Injectable()
export class AuthAdminService implements OnModuleInit {
  constructor(private prisma: PrismaService) {}

  async onModuleInit() {
    // create default admin if no admin exists
    const adminCount = await this.prisma.adminUser.count({
      where: { role: 'ADMIN' },
    });

    if (adminCount === 0) {
      const passwordHash = await bcrypt.hash('admin', 10);
      await this.prisma.adminUser.create({
        data: {
          username: 'admin',
          passwordHash,
          fullName: 'Super Administrator',
          role: 'ADMIN',
        },
      });
      console.log('Default admin user created: admin / admin');
    }
  }

  async login(dto: AdminLoginDto) {
    const user = await this.prisma.adminUser.findUnique({
      where: { username: dto.username },
    });

    if (!user || !user.isActive) {
      throw new UnauthorizedException('Sai tài khoản hoặc mật khẩu.');
    }

    const isMatch = await bcrypt.compare(dto.password, user.passwordHash);
    if (!isMatch) {
      throw new UnauthorizedException('Sai tài khoản hoặc mật khẩu.');
    }

    // Since we don't have JWT, we can just return a pseudo-token or user info
    // The desktop app can keep track of who is logged in.
    return {
      success: true,
      user: {
        id: user.id,
        username: user.username,
        fullName: user.fullName,
        role: user.role,
      },
      // pseudo token for future use
      token: `${user.id}_${Date.now()}`,
    };
  }

  async createStaff(dto: CreateAdminUserDto) {
    const existing = await this.prisma.adminUser.findUnique({
      where: { username: dto.username },
    });
    if (existing) {
      throw new BadRequestException('Tên đăng nhập đã tồn tại.');
    }

    const passwordHash = await bcrypt.hash(dto.password, 10);
    const user = await this.prisma.adminUser.create({
      data: {
        username: dto.username,
        passwordHash,
        fullName: dto.fullName || dto.username,
        role: 'STAFF',
      },
    });

    return {
      id: user.id,
      username: user.username,
      fullName: user.fullName,
      role: user.role,
    };
  }

  async getUsers() {
    const users = await this.prisma.adminUser.findMany({
      orderBy: { createdAt: 'asc' },
    });
    return users.map((u) => ({
      id: u.id,
      username: u.username,
      fullName: u.fullName,
      role: u.role,
      isActive: u.isActive,
      createdAt: u.createdAt,
    }));
  }

  async deleteUser(id: string) {
    const user = await this.prisma.adminUser.findUnique({ where: { id } });
    if (!user) {
      throw new BadRequestException('Không tìm thấy tài khoản.');
    }
    if (user.role === 'ADMIN') {
      throw new BadRequestException('Không thể xóa tài khoản ADMIN.');
    }

    await this.prisma.adminUser.delete({ where: { id } });
    return { success: true };
  }

  async updateAdminAccount(dto: CreateAdminUserDto) {
    const admin = await this.prisma.adminUser.findFirst({
      where: { role: 'ADMIN' },
    });

    if (!admin) {
      throw new BadRequestException('Không tìm thấy tài khoản ADMIN.');
    }

    const passwordHash = await bcrypt.hash(dto.password, 10);
    await this.prisma.adminUser.update({
      where: { id: admin.id },
      data: {
        username: dto.username,
        passwordHash,
        fullName: dto.fullName,
      },
    });

    return { success: true };
  }
}
