import { PrismaClient } from '@prisma/client';

const prisma = new PrismaClient();

async function main() {
  await prisma.pricingConfig.upsert({
    where: { name: 'Default Rate' },
    update: {
      pricePerMinute: 3000,
      isActive: true,
    },
    create: {
      name: 'Default Rate',
      pricePerMinute: 3000,
      isActive: true,
    },
  });
}

main()
  .then(async () => {
    await prisma.$disconnect();
  })
  .catch(async (error) => {
    console.error(error);
    await prisma.$disconnect();
    process.exit(1);
  });
