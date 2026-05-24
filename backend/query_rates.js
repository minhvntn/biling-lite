const { PrismaClient } = require('@prisma/client');
const prisma = new PrismaClient();

async function main() {
  const groups = await prisma.pcGroup.findMany();
  console.log('PC Groups in DB:');
  console.log(JSON.stringify(groups, null, 2));

  const settings = await prisma.appSetting.findMany();
  console.log('App Settings in DB:');
  console.log(JSON.stringify(settings, null, 2));
}

main()
  .catch(console.error)
  .finally(() => prisma.$disconnect());
