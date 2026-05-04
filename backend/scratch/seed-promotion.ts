import { PrismaClient } from '@prisma/client';

async function main() {
  const prisma = new PrismaClient();
  try {
    const existing = await prisma.timeBasedPromotion.findFirst({
      where: { name: 'Khuyến mãi giờ vàng' }
    });

    if (!existing) {
      await prisma.timeBasedPromotion.create({
        data: {
          name: 'Khuyến mãi giờ vàng',
          daysOfWeek: [1, 2, 3, 4, 5], // Mon-Fri
          startTime: '07:00',
          endTime: '17:00',
          discountPercent: 10,
        }
      });
      console.log('Seeded promotion successfully');
    } else {
      console.log('Promotion already exists');
    }
  } catch (error) {
    console.error('Error seeding promotion:', error);
  } finally {
    await prisma.$disconnect();
  }
}

main();
